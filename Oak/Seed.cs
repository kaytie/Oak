﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Reflection;
using System.Text.RegularExpressions;
using Oak.Extensions;
using System.Diagnostics;
using System.IO;

namespace Oak
{
    public class Seed
    {
        public virtual ConnectionProfile ConnectionProfile { get; set; }
        SyntaxVariations syntax;

        public Seed()
            : this(null)
        {
        }

        public Seed(ConnectionProfile connectionProfile)
        {
            if (connectionProfile == null) connectionProfile = new ConnectionProfile();

            ConnectionProfile = connectionProfile;

            syntax = new SyntaxVariations(connectionProfile);
        }

        /// <summary>
        /// Generates script for creating a table.  Use ExecuteNonQuery() 
        /// to execute script.  ExecuteNonQuery is an 
        /// extension method on string (if you dont see the extension method, 
        /// make sure you have using Oak; in your cs file.
        /// </summary>
        /// <returns>Returns the sql command generated by seed.</returns>
        public string CreateTable(string table, params dynamic[] columns)
        {
            return CreateTable(syntax.Dbo(), table, columns);
        }

        /// <summary>
        /// Generates script for creating a table.  Use ExecuteNonQuery() 
        /// to execute script.  ExecuteNonQuery is an 
        /// extension method on string (if you dont see the extension method, 
        /// make sure you have using Oak; in your cs file.
        /// </summary>
        /// <returns>Returns the sql command generated by seed.</returns>
        public string CreateTable(string schema, string table, params dynamic[] columns)
        {
            var primaryKeyColumns = new List<string>();

            var otherColumns = new List<string>();

            foreach (var entry in columns)
            {
                object column = entry;

                if (column.IsPrimaryKey()) primaryKeyColumns.Add(column.PrimaryKeyColumn());

                otherColumns.Add(ColumnStringFor(column));
            }

            return CreateTableCommand(schema, table, otherColumns, primaryKeyColumns);
        }

        public string DropTable(string table)
        {
            return "drop table " + syntax.Escape(table);
        }

        /// <summary>
        /// Generates script to create columns.  Use ExecuteNonQuery()
        /// to execute script.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public string AddColumns(string table, params dynamic[] columns)
        {
            return AddColumns("dbo", table, columns);
        }

        public string CreateSchema(string schema)
        {
            return "CREATE SCHEMA {0}".With(schema);
        }
        /// <summary>
        /// Generates script to create columns.  Use ExecuteNonQuery()
        /// to execute script.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public string AddColumns(string schema, string table, params dynamic[] columns)
        {
            string columnString = "";

            for (int i = 0; i < columns.Length; i++)
            {
                object column = columns[i];

                columnString += ColumnStringFor(column);

                if (i != columns.Length - 1) columnString += ", ";
            }

            return "ALTER TABLE [{0}].[{1}] ADD {2}"
                .With(schema, table, columnString);
        }

        string ColumnStringFor(object column)
        {
            var name = syntax.Escape(column.Name());
            var type = column.SqlType();
            var defaultValue = column.DefaultValue();
            var isIdentity = column.IsIdentityColumn();
            var isPrimaryKey = column.IsPrimaryKey();
            var isForeignKey = column.IsForeignKeyColumn();

            string identityAsString = isIdentity ? syntax.IdentityScript() : "";
            string foreignKeyString = isForeignKey ? " FOREIGN KEY REFERENCES " + column.ForeignKey() : "";

            return "{0} {1} {2} {3}{4}{5}"
                        .With(name,
                            type,
                            column.NullDefinition(),
                            column.DefaultValueDefinition(),
                            identityAsString,
                            foreignKeyString)
                        .ReplaceSequentialSpacesWithSingleSpace()
                        .Trim();
        }


        string CreateTableCommand(string schema, string table, List<string> otherColumns, List<string> primaryKeyColumns)
        {
            var primaryKeyColumnScript = string.Join(",", primaryKeyColumns.Select(syntax.ConstraintColumn));

            var columns = string.Join(",", otherColumns);

            var primaryKeyScript = syntax.ConstraintPrimaryKey(table, primaryKeyColumnScript);

            if (!primaryKeyColumns.Any()) primaryKeyScript = "";

            return "CREATE TABLE {0}({1}{2})".With(FullyQualifedTableName(schema, table), columns, primaryKeyScript);
        }

        string FullyQualifedTableName(string schema, string table)
        {
            schema = syntax.Escape(schema);
            table = syntax.Escape(table);

            if(string.IsNullOrEmpty(schema)) return table;

            return schema + "." + table;
        }

        public void PurgeDb()
        {
            DropAllForeignKeys();
            DropAllPrimaryKeys();
            DropAllTables();
            DropCustomSchemas();
        }

        private void DropCustomSchemas()
        {
            if(syntax.IsPostgres()) return; //TODO

            var reader = @"SELECT name FROM sys.schemas".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                var schemaName = reader["name"];

                if (!DefaultSchemas().Contains(schemaName))
                {
                    "drop schema [{0}] ".With(schemaName).ExecuteNonQuery(ConnectionProfile);
                }
            }
        }

        List<string> DefaultSchemas()
        {
            return new List<string> 
            {
                "dbo",
                "guest",
                "INFORMATION_SCHEMA",
                "sys",
                "db_owner",
                "db_accessadmin",
                "db_securityadmin",
                "db_ddladmin",
                "db_backupoperator",
                "db_datareader",
                "db_datawriter",
                "db_denydatareader",
                "db_denydatawriter"
            };
        }

        void DropAllForeignKeys()
        {
            if(syntax.IsPostgres()) return; //TODO

            var reader = @"
            select  name as constraint_name,
                    object_name(parent_obj) as table_name,
                    object_schema_name(parent_obj) as table_schema
            from sysobjects where xtype = 'f'".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "alter table [{0}].[{1}] drop constraint {2} ".With(reader["table_schema"], reader["table_name"], reader["constraint_name"]).ExecuteNonQuery(ConnectionProfile);
            }
        }

        void DropAllPrimaryKeys()
        {
            if(syntax.IsPostgres()) return; //TODO

            var reader = @"
            select  name as constraint_name, 
                    object_name(parent_obj) as table_name,
                    object_schema_name(parent_obj) as table_schema 
            from sysobjects where xtype = 'pk'".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "alter table [{0}].[{1}] drop constraint {2} ".With(reader["table_schema"], reader["table_name"], reader["constraint_name"]).ExecuteNonQuery(ConnectionProfile);
            }
        }

        void DropAllTables()
        {
            var query = @"
            select  name as table_name, 
                    object_schema_name(id) as table_schema 
            from sysobjects where xtype = 'u'";

            if(syntax.IsPostgres())
            {
                query = @"
                select table_name,
                       table_schema
                from information_schema.tables
                where table_schema = 'public'";
            }

            var reader = query.ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "drop table {0}.{1} ".With(syntax.Escape(reader["table_schema"] as string), syntax.Escape(reader["table_name"] as string)).ExecuteNonQuery(ConnectionProfile);
            }
        }

        public void Export(string exportPath, IEnumerable<dynamic> scripts)
        {
            int order = 1;

            scripts.ForEach<dynamic>(s =>
            {
                var result = s();

                string name = s.Method.Name;

                if (result is string) WriteSqlFile(exportPath, "{0} - {1}".With(order, name), result);

                else
                {
                    int parts = 1;

                    foreach (var r in result)
                    {
                        WriteSqlFile(exportPath, "{0} - {1} - {2}".With(order, parts, name), r);

                        parts++;
                    }
                }

                order++;
            });
        }

        void WriteSqlFile(string path, string name, string content)
        {
            File.WriteAllText(Path.Combine(path, name + ".sql"), content);
        }

        public object Id()
        {
            if(syntax.IsPostgres())
            {
                return new
                {
                    Id = "SERIAL",
                    Identity = true,
                    PrimaryKey = true
                };
            }

            return new { Id = "int", Identity = true, PrimaryKey = true };
        }

        public object GuidId()
        {
            return new { Id = "uniqueidentifier", PrimaryKey = true };
        }

        public void ExecuteNonQuery(dynamic script)
        {
            if (script is Delegate) ExecuteNonQuery(script());

            else if(script is string) (script as string).ExecuteNonQuery(ConnectionProfile);

            else foreach (var s in script) (s as string).ExecuteNonQuery(ConnectionProfile);
        }

        public object ExecuteScalar(string sql)
        {
            return sql.ExecuteScalar(ConnectionProfile);
        }

        public string DisableKeyConstaints()
        {
            return "EXEC sp_msforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT all';";
        }

        public string EnableKeyConstraints()
        {
            return "EXEC sp_msforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all';";
        }

        public void DeleteAllRecords()
        {
            DisableKeyConstaints().ExecuteNonQuery(ConnectionProfile);

            "EXEC sp_msforeachtable 'delete ?';".ExecuteNonQuery(ConnectionProfile);

            EnableKeyConstraints().ExecuteNonQuery(ConnectionProfile);
        }

        public string RenameColumn(string schema, string table, string currentColumnName, string newColumnName)
        {
            return "sp_rename '[{0}].[{1}].[{2}]', '{3}', 'COLUMN'".With(schema, table, currentColumnName, newColumnName);
        }

        public string RenameColumn(string table, string currentColumnName, string newColumnName)
        {
            return RenameColumn("dbo", table, currentColumnName, newColumnName);
        }

        public string DropColumn(string schema, string table, string column)
        {
            return "alter table [{0}].[{1}] drop column {2}".With(schema, table, column);
        }

        public string DropColumn(string table, string column)
        {
            return DropColumn("dbo", table, column);
        }

        public string DropConstraint(string schema, string table, string forColumn)
        {
            var name = @"
            select CONSTRAINT_NAME
            from INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
            where CONSTRAINT_SCHEMA = '{0}' and TABLE_NAME = '{1}'
            and COLUMN_NAME = '{2}'".With(schema, table, forColumn).ExecuteScalar(ConnectionProfile);

            return "alter table [{0}].[{1}] drop constraint {2}".With(schema, table, name);
        }

        public string DropConstraint(string table, string forColumn)
        {
            return DropConstraint("dbo", table, forColumn);
        }

        public void ExecuteUpTo(IEnumerable<Func<dynamic>> scripts, Func<dynamic> method)
        {
            foreach (Func<dynamic> script in scripts)
            {
                if (script.Method == method.Method) break;

                ExecuteNonQuery(script());
            }
        }

        public void ExecuteTo(IEnumerable<Func<dynamic>> scripts, Func<dynamic> method)
        {
            foreach (Func<dynamic> script in scripts)
            {
                ExecuteNonQuery(script());

                if (script.Method == method.Method) break;
            }
        }
    }

    public class SyntaxVariations
    {
        ConnectionProfile connectionProfile;
        public SyntaxVariations(ConnectionProfile connectionProfile)
        {
            this.connectionProfile = connectionProfile;
        }

        public bool IsPostgres()
        {
            return connectionProfile.ProviderName == "Npgsql";
        }

        public string Escape(string s)
        {
            if(IsPostgres()) return s;

            return "[" + s + "]";
        }

        public string Dbo()
        {
            if (IsPostgres()) return "public";

            return "dbo";
        }

        public string IdentityScript()
        {
            if (IsPostgres()) return "";

            return " IDENTITY(1,1)";
        }

        public string PrimaryKeyConstraintName(string forTable)
        {
            if (IsPostgres()) return "PK_{0}".With(forTable);

            return "[PK_{0}]".With(forTable);
        }

        public string ConstraintPrimaryKey(string table, string primaryKeyColumnScript)
        {
            if (IsPostgres()) return ", CONSTRAINT {0} PRIMARY KEY ({1})".With(PrimaryKeyConstraintName(table), primaryKeyColumnScript ?? string.Empty);

            return ", CONSTRAINT {0} PRIMARY KEY CLUSTERED ({1})".With(PrimaryKeyConstraintName(table), primaryKeyColumnScript ?? string.Empty);
        }

        public string ConstraintColumn(string column)
        {
            var escaped = Escape(column);

            if(IsPostgres()) return escaped;

            return escaped + " ASC";
        }
    }
}

namespace Oak.Extensions
{
    public static class StringExtensions
    {
        public static string With(this string s, params object[] args)
        {
            return string.Format(s, args);
        }

        public static string ReplaceSequentialSpacesWithSingleSpace(this string s)
        {
            var single = Regex.Replace(s, @"[ ]{2,}", " ");

            return single;
        }
    }

    public static class ColumnExtensions
    {
        public static bool AllowsNulls(this object columnDefinition)
        {
            return !columnDefinition.Properties().Has("Nullable", withValue: false, @in: columnDefinition);
        }

        public static bool IsIdentityColumn(this object columnDefinition)
        {
            return columnDefinition.Properties().Has("Identity", withValue: true, @in: columnDefinition);
        }

        public static bool IsForeignKeyColumn(this object columnDefinition)
        {
            return columnDefinition.Properties().Has("ForeignKey", @in: columnDefinition);
        }

        public static object ForeignKey(this object columnDefinition)
        {
            return columnDefinition.Properties().Get("ForeignKey", @in: columnDefinition);
        }

        public static bool IsPrimaryKey(this object columnDefinition)
        {
            return columnDefinition.Properties().Has("PrimaryKey", withValue: true, @in: columnDefinition);
        }

        public static object DefaultValue(this object columnDefinition)
        {
            return columnDefinition.Properties().Get("Default", @in: columnDefinition);
        }

        public static object DefaultValueDefinition(this object columnDefinition)
        {
            var defaultValue = columnDefinition.DefaultValue();

            string defaultAsString = "";

            if (defaultValue != null) defaultAsString = "DEFAULT('{0}')".With(defaultValue.ToString());
            else return "";

            var reservedStrings = new[] { "getdate()", "newid()", "getutcdate()" };

            if (reservedStrings.Contains(defaultValue.ToString().ToLower())) defaultAsString = "DEFAULT({0})".With(defaultValue.ToString());

            return defaultAsString;
        }

        public static string NullDefinition(this object columnDefinition)
        {
            var nullDefinition = columnDefinition.AllowsNulls() ? "NULL" : "NOT NULL";

            if (columnDefinition.IsPrimaryKey() || columnDefinition.IsIdentityColumn()) nullDefinition = "NOT NULL";

            return nullDefinition;
        }

        public static string PrimaryKeyColumn(this object columnDefinition)
        {
            if (columnDefinition.IsPrimaryKey())
            {
                return columnDefinition.Name();
            }

            return null;
        }

        public static string Name(this object columnDefinition)
        {
            return columnDefinition.Properties().First().Name;
        }

        public static bool Has(this PropertyInfo[] properties, string name, bool withValue, object @in)
        {
            return properties.Any(s => s.Name == name && Convert.ToBoolean(s.GetValue(@in, null)) == withValue);
        }

        public static bool Has(this PropertyInfo[] properties, string name, object @in)
        {
            return properties.Any(s => s.Name == name);
        }

        public static object Get(this PropertyInfo[] properties, string name, object @in)
        {
            var property = properties.SingleOrDefault(s => s.Name == name);

            if (property == null) return null;

            return property.GetValue(@in, null);
        }

        public static string Value(this PropertyInfo propertyInfo, object o)
        {
            return propertyInfo.GetValue(o, null).ToString();
        }

        public static string SqlType(this object columnDefinition)
        {
            return columnDefinition.Properties().First().Value(columnDefinition);
        }

        private static PropertyInfo[] Properties(this object columnDefinition)
        {
            return columnDefinition.GetType().GetProperties();
        }
    }
}
