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
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using System.Threading;

using System.Windows.Forms;

using NFX.ApplicationModel;
using NFX.ApplicationModel.Volatile;

using NFX.Log;
using NFX.Environment;
using NFX.DataAccess;


using NFX.WinForms.Themes;
using NFX.WinForms.Themes.Default;
using NFX.Instrumentation;
using NFX.ServiceModel;
using NFX.Throttling;
using NFX.Glue;
using NFX.Security;
using NFX.Time;

namespace NFX.WinForms
{
  /// <summary>
  /// Provides base implementation of IApplication for WinForms. This class is NOT thread safe
  /// </summary>
  public class BaseApplication : CommonApplicationLogic
  {

    #region .ctor
      
      /// <summary>
      /// Takes optional args[] and root configuration. If configuration is null then
      ///  application is configured from a file co-located with entry-point assembly and
      ///   called the same name as assembly with '.config' extension, unless args are specified and "/config file"
      ///   switch is used in which case 'file' has to be locatable and readable.
      /// </summary>
      public BaseApplication(string[] args, ConfigSectionNode rootConfig)
      {
        lock(typeof(BaseApplication))
        {
          if (s_Instance != null) throw new WFormsException(StringConsts.APP_INSTANCE_ALREADY_CREATED_ERROR);

          try
          {  
            Configuration argsConfig;
            if (args != null)
              argsConfig = new CommandArgsConfiguration(args);
            else
              argsConfig = new MemoryConfiguration();

            m_CommandArgs = argsConfig.Root;

            m_ConfigRoot = rootConfig ?? GetConfiguration().Root;

            InitApplication();
        
            s_Instance = this;

          }
          catch
          {
              Destructor();
              throw;
          }

        }
      }


      protected override void Destructor()
      {
         lock(typeof(BaseApplication))
         {
            base.Destructor();
            CleanupApplication();
            s_Instance = null;
         }
      }
    

    #endregion

    #region Fields

      protected static BaseApplication s_Instance;
      
      
      protected static ITheme s_Theme;
      
      
      protected ConfigSectionNode m_CommandArgs;
      
    #endregion
    
  
    #region Properties

      /// <summary>
      /// References a singleton instance of BaseApplication
      /// </summary>
      public static BaseApplication Instance
      {
        get
        {
          if (s_Instance==null)
            throw new WFormsException(StringConsts.APP_INSTANCE_NULL_ERROR);
           
          return s_Instance;
        }
      }
      
      /// <summary>
      /// Indicates whether application context was allocated  
      /// </summary>
      public static bool ContextAvailable 
      {
        get
        {
          return s_Instance!=null;
        }
      }
      
      
      #region IApplication Members

        /// <summary>
        /// Returns application name
        /// </summary>
        public static string AppName
        {
          get { return Instance.Name; }
        }
        
        
        /// <summary>
        /// Returns true when application instance is active and working. This property returns false as soon as application finalization starts on shutdown
        /// Use to exit long-running loops and such
        /// </summary>
        public static bool IsActive
        {
          get { return Instance.Active; }
        }
        
        
        /// <summary>
        /// References application logger
        /// </summary>
        public static ILog Logger
        {
          get { return Instance.Log; }
        }
        

        /// <summary>
        /// References application instrumentation
        /// </summary>
        public static IInstrumentation Instruments
        {
          get { return Instance.Instrumentation; }
        }

        /// <summary>
        /// References application throttling
        /// </summary>
        public static IThrottling Throttles
        {
            get { return Instance.Throttling; }
        }

        /// <summary>
        /// References command line arguments configuration, so one can use it as:
        ///  <code>
        ///   if (NFXApplication.Instance.CommandArgs["DeleteFiles"].Exists)
        ///   {
        ///     // this will run if process was invoked like so: process.exe /deleteFiles 
        ///   }
        ///  </code>
        /// </summary>
        public ConfigSectionNode CommandArgs
        {
          get { return m_CommandArgs; }
        }

        /// <summary>
        /// References command line arguments configuration, so one can use it as:
        ///  <code>
        ///   if (NFXApplication.Instance.CommandArgs["DeleteFiles"].Exists)
        ///   {
        ///     // this will run if process was invoked like so: process.exe /deleteFiles 
        ///   }
        ///  </code>
        /// </summary>
        public static ConfigSectionNode CmdArgs
        {
          get { return Instance.CommandArgs; }
        }
        

        /// <summary>
        /// Provides access to configuration root for the whole application
        /// </summary>
        public static ConfigSectionNode ConfRoot
        {
          get { return Instance.ConfigRoot; }
        }
        
        /// <summary>
        /// References application data store
        /// </summary>
        public static IDataStore Data
        {
          get { return Instance.DataStore; }
        }

        /// <summary>
        /// References application object store. Objects will survive application termination 
        /// </summary>
        public static IObjectStore Objects
        {
          get { return Instance.ObjectStore; }
        }
        

        /// <summary>
        /// References glue that can be used to connect to remote entities
        /// </summary>
        public static IGlue Glues
        {
          get { return Instance.Glue; }
        }

        /// <summary>
        /// References security manager that performs user authentication based on passed credentials and other security-related global tasks
        /// </summary>
        public static ISecurityManager Security
        {
          get { return Instance.SecurityManager; }
        }

        /// <summary>
        /// References time source - an entity that supplies local and UTC times. The concrete implementation
        ///  may elect to get accurate times from the network or other external precision time sources (i.e. NASA atomic clock)
        /// </summary>
        public static ITimeSource Time
        {                                   
          get { return Instance.TimeSource;}
        }

    #endregion
                              
      
      
      /// <summary>
      /// Specifies current application theme. If this property was never set, a default theme will be created
      /// </summary>
      public static ITheme Theme
      {
        get
        {
         if (s_Theme == null)
          s_Theme = new DefaultTheme();
         
         return s_Theme;
        }
      }
      
      
      
      
      
    #endregion
    
    
    #region Public

      /// <summary>
      /// Destroys application effectively finalizing all services
      /// </summary>
      public static void Destroy()
      {
        Instance.Dispose();
        s_Instance = null;
      }


      /// <summary>
      /// Sets application theme, pass null to select default theme
      /// </summary>
      public static void SetTheme(Type themeType)
      {
        if (s_Theme!=null)
        {
          s_Theme.Dispose();
          s_Theme = null; // in case of unhandeled exception below
        }  
        
        if (themeType != null)
        {
          s_Theme = Activator.CreateInstance(themeType) as ITheme;
          
          if (s_Theme==null)
            throw new WFormsException(string.Format(StringConsts.THEME_CREATION_ERROR, themeType.ToString()));
        }
        RefreshAllForms();
      }
      
      
      /// <summary>
      /// Refreshes all open application forms
      /// </summary>
      public static void RefreshAllForms()
      {
        foreach (Form frm in Application.OpenForms)
          frm.Refresh();
      }
    
    #endregion


    #region Protected

      protected virtual Configuration GetConfiguration()
      {
          //try to read from  /config file
          var configFile = m_CommandArgs[CONFIG_SWITCH].AttrByIndex(0).Value;

          if (string.IsNullOrEmpty(configFile))
              configFile = GetDefaultConfigFileName();
                  

          Configuration conf;

          if (File.Exists(configFile))
              conf = Configuration.ProviderLoadFromFile(configFile);
          else
              conf = new MemoryConfiguration();

          return conf;
      }

    #endregion
  }
  
}
