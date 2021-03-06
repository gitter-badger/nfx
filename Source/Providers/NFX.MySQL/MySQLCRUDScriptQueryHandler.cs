/*<FILE_LICENSE>
* NFX (.NET Framework Extension) Unistack Library
* Copyright 2003-2014 IT Adapter Inc / 2015 Aum Code LLC
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
</FILE_LICENSE>*/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MySql.Data.MySqlClient;

using NFX.DataAccess.CRUD;          

namespace NFX.DataAccess.MySQL
{
    /// <summary>
    /// Executes MySql CRUD script-based queries
    /// </summary>
    public sealed class MySQLCRUDScriptQueryHandler : ICRUDQueryHandler   
    {
        #region .ctor
            public MySQLCRUDScriptQueryHandler(MySQLDataStore store, QuerySource source)
            {
                m_Store = store;
                m_Source = source;
            }
        #endregion

        #region Fields
            private MySQLDataStore m_Store;
            private QuerySource m_Source;
        #endregion

        #region ICRUDQueryHandler
            public string Name
            {
                get { return m_Source.Name; }
            }

            public ICRUDDataStore Store
            {
               get { return m_Store;}
            }


            public Schema GetSchema(ICRUDQueryExecutionContext context, Query query)
            {
                var ctx = (MySQLCRUDQueryExecutionContext)context;
                var target = ctx.DataStore.TargetName;

                using (var cmd = ctx.Connection.CreateCommand())
                {
                    cmd.CommandText =  m_Source.StatementSource;

                    
                    PopulateParameters(cmd, query);
                              
                    

                    cmd.Transaction = ctx.Transaction;

                    MySqlDataReader reader = null;

                    try
                    {
                        reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly);
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-ok", cmd, null);
                    }
                    catch(Exception error)
                    {
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-error", cmd, error);
                        throw;
                    }


                    using (reader)
                    {
                      Schema.FieldDef[] toLoad;
                      return GetSchemaForQuery(target, query, reader, m_Source, out toLoad);
                    }//using reader
                }//using command
            }


            public Task<Schema> GetSchemaAsync(ICRUDQueryExecutionContext context, Query query)
            {
              return TaskUtils.AsCompletedTask( () => this.GetSchema(context, query));
            }


            public RowsetBase Execute(ICRUDQueryExecutionContext context, Query query, bool oneRow = false)
            {
                var ctx = (MySQLCRUDQueryExecutionContext)context;
                var target = ctx.DataStore.TargetName;

                using (var cmd = ctx.Connection.CreateCommand())
                {
                    
                    cmd.CommandText =  m_Source.StatementSource;
                   
                    PopulateParameters(cmd, query);
                              
                   

                    cmd.Transaction = ctx.Transaction;

                    MySqlDataReader reader = null;

                    try
                    {
                        reader = oneRow ? cmd.ExecuteReader(CommandBehavior.SingleRow) : cmd.ExecuteReader();
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-ok", cmd, null);
                    }
                    catch(Exception error)
                    {
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-error", cmd, error);
                        throw;
                    }

                    using (reader)
                      return PopulateRowset(reader, target, query, m_Source, oneRow);
                }//using command
            }

            public Task<RowsetBase> ExecuteAsync(ICRUDQueryExecutionContext context, Query query, bool oneRow = false)
            {
              return TaskUtils.AsCompletedTask( () => this.Execute(context, query, oneRow));
            }


            public int ExecuteWithoutFetch(ICRUDQueryExecutionContext context, Query query)
            {
                var ctx = (MySQLCRUDQueryExecutionContext)context;

                using (var cmd = ctx.Connection.CreateCommand())
                {

                    cmd.CommandText =  m_Source.StatementSource;
                    
                    PopulateParameters(cmd, query);
                                                                                    

                    cmd.Transaction = ctx.Transaction;

                    try
                    {
                        var affected = cmd.ExecuteNonQuery();
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-ok", cmd, null);
                        return affected;
                    }
                    catch(Exception error)
                    {
                        GeneratorUtils.LogCommand(ctx.DataStore.LogLevel, "queryhandler-error", cmd, error);
                        throw;
                    }
                }//using command
            }

            public Task<int> ExecuteWithoutFetchAsync(ICRUDQueryExecutionContext context, Query query)
            {
               return TaskUtils.AsCompletedTask( () => this.ExecuteWithoutFetch(context, query));
            }

        #endregion

        #region Static Helpers

            /// <summary>
            /// Reads data from reader into rowset. th reader is NOT disposed 
            /// </summary>
            public static Rowset PopulateRowset(MySqlDataReader reader, string target, Query query, QuerySource qSource, bool oneRow)
            {
              Schema.FieldDef[] toLoad;
              Schema schema = GetSchemaForQuery(target, query, reader, qSource, out toLoad);

              var result = new Rowset(schema);
              while(reader.Read())
              {
                var row = Row.MakeRow(schema, query.ResultRowType);
                        
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var fdef = toLoad[i];
                    if (fdef==null) continue;

                    var val = reader.GetValue(i);
                    if (fdef.NonNullableType==typeof(bool))
                        row[fdef.Order] = val.AsNullableBool();
                    else
                        row[fdef.Order] = val;
                }

                result.Add( row );
                if (oneRow) break;
              }

              return result;
            }
            
            /// <summary>
            /// Populates MySqlCommand with parameters from CRUD Query object
            /// Note: this code was purposely made provider specific because other providers may treat some nuances differently
            /// </summary>
            public void PopulateParameters(MySqlCommand cmd, Query query)
            {
               foreach(var par in query.Where(p=> p.HasValue))
                cmd.Parameters.AddWithValue(par.Name, par.Value);
               
               if (query.StoreKey!=null)
               {                               
                var where = GeneratorUtils.KeyToWhere(query.StoreKey, cmd.Parameters);
                cmd.CommandText += "\n WHERE \n {0}".Args( where );
               }

               CRUDGenerator.ConvertParameters(m_Store, cmd.Parameters);
            }

            /// <summary>
            /// Gets CRUD schema from MySqlReader per particular QuerySource.
            /// If source is null then all columns from reader are copied.
            /// Note: this code was purposely made provider specific because other providers may treat some nuances differently
            /// </summary>
            public static Schema GetSchemaFromReader(string name, QuerySource source, MySqlDataReader reader)
            {
               var table = name;
               var fdefs = new List<Schema.FieldDef>();

               for (int i = 0; i < reader.FieldCount; i++)
               {                        
                    var fname = reader.GetName(i);
                    var ftype = reader.GetFieldType(i);

                    var def = new Schema.FieldDef(fname, ftype, source!=null ? ( source.HasPragma ? source.ColumnDefs[fname] : null) : null);
                    fdefs.Add( def );
               }

               if (source!=null)
                if (source.HasPragma && source.ModifyTarget.IsNotNullOrWhiteSpace()) table = source.ModifyTarget;

               if (table.IsNullOrWhiteSpace()) table = Guid.NewGuid().ToString();
                
               return new Schema(table, source!=null ? source.ReadOnly : true,  fdefs);
            }

            /// <summary>
            /// Gets schema from reader taking Query.ResultRowType in consideration
            /// </summary>
            public static Schema GetSchemaForQuery(string target, Query query, MySqlDataReader reader, QuerySource qSource, out Schema.FieldDef[] toLoad)
            {
              Schema schema;
              var rtp = query.ResultRowType;

              if (rtp != null && typeof(TypedRow).IsAssignableFrom(rtp))
                schema = Schema.GetForTypedRow(query.ResultRowType);
              else
                schema = GetSchemaFromReader(query.Name, qSource, reader); 
                      
              //determine what fields to load
              toLoad = new Schema.FieldDef[reader.FieldCount];
              for (int i = 0; i < reader.FieldCount; i++)
              {
                var name = reader.GetName(i);
                var fdef = schema[name];
                if (fdef==null) continue;
                var attr =  fdef[target];
                if (attr!=null)
                {
                  if (attr.StoreFlag!=StoreFlag.LoadAndStore && attr.StoreFlag!=StoreFlag.OnlyLoad) continue;
                }
                toLoad[i] = fdef;
              }

              return schema;
            }

        #endregion

    }

    
    


}
