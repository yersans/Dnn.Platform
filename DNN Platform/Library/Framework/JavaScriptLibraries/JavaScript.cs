﻿#region Copyright

// 
// DotNetNuke® - http://www.dotnetnuke.com
// Copyright (c) 2002-2013
// by DotNetNuke Corporation
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions 
// of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

#endregion

#region Usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Host;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Users;
using DotNetNuke.Instrumentation;
using DotNetNuke.Services.Installer.Packages;
using DotNetNuke.Services.Localization;
using DotNetNuke.Services.Log.EventLog;
using DotNetNuke.UI.Skins;
using DotNetNuke.UI.Skins.Controls;
using DotNetNuke.UI.Utilities;
using DotNetNuke.Web.Client;
using DotNetNuke.Web.Client.ClientResourceManagement;
using Globals = DotNetNuke.Common.Globals;

#endregion

namespace DotNetNuke.Framework.JavaScriptLibraries
{
    public class JavaScript
    {
        private const string ScriptPreix = "JSL.";
        private const string LegacyPrefix = "LEGACY.";

        private static readonly ILog Logger = LoggerSource.Instance.GetLogger(typeof (JavaScript));

        #region Private Properties

        /// <summary>
        ///     checks whether the script file is a known javascript library
        /// </summary>
        /// <param name="jsname">script identifier</param>
        /// <returns></returns>
        public static bool IsInstalled(String jsname)
        {
            JavaScriptLibrary library = JavaScriptLibraryController.Instance.GetLibrary(l => l.LibraryName == jsname);
            return library != null;
        }

        /// <summary>
        ///     determine whether to use the debug script for a file
        /// </summary>
        /// <returns>whether to use the debug script</returns>
        public static bool UseDebugScript()
        {
            if (Globals.Status != Globals.UpgradeStatus.None)
            {
                return false;
            }
            return HttpContext.Current.IsDebuggingEnabled;
        }

        /// <summary>
        ///     returns the version of a javascript library from the database
        /// </summary>
        /// <param name="jsname">the library name</param>
        /// <returns></returns>
        public static string Version(String jsname)
        {
            JavaScriptLibrary library =
                JavaScriptLibraryController.Instance.GetLibrary(l => l.LibraryName == jsname);
            return library != null ? Convert.ToString(library.Version) : String.Empty;
        }

        /// <summary>
        ///     adds a request for a script into the page items collection
        /// </summary>
        /// <param name="jsname">the library name</param>
        public static void RequestRegistration(String jsname)
        {
            //if we're installing, packages are not available so skip
            if (IsInstallationUrl())
            {
                AddPreInstallorLegacyItemRequest(jsname);
                return;
            }
            //handle case where script has no javascript library
            switch (jsname)
            {
                case CommonJs.DnnPlugins:
                case CommonJs.HoverIntent:
                case CommonJs.jQueryFileUpload:
                    AddPreInstallorLegacyItemRequest(jsname);
                    return;
            }

            JavaScriptLibrary library = GetHighestVersionLibrary(jsname);
            AddItemRequest(library.JavaScriptLibraryID);
        }

        private static void AddPreInstallorLegacyItemRequest(string jsl)
        {
            HttpContext.Current.Items[LegacyPrefix + jsl] = true;
        }


        /// <summary>
        ///     adds a request for a script into the page items collection
        /// </summary>
        /// <param name="jsname">the library name</param>
        public static void RequestRegistration(String jsname, Version version)
        {
            JavaScriptLibrary library = JavaScriptLibraryController.Instance.GetLibrary(l => l.Version == version);
            if (library != null)
            {
                AddItemRequest(library.JavaScriptLibraryID);
            }
            else
            {
                //this will only occur if a specific library is requested and not available
                //TODO: should we update to any available version?
                Logger.TraceFormat("Missing Library request - {0} : {1}", jsname, version.ToString());
            }
        }


        /// <summary>
        ///     adds a request for a script into the page items collection
        /// </summary>
        /// <param name="jsname">the library name</param>
        public static void RequestRegistration(String jsname, Version version, SpecificVersion specific)
        {
            JavaScriptLibrary library;
            bool isProcessed = false;
            switch (specific)
            {
                case SpecificVersion.Latest:
                    library = GetHighestVersionLibrary(jsname);
                    AddItemRequest(library.JavaScriptLibraryID);
                    isProcessed = true;
                    break;
                case SpecificVersion.LatestMajor:
                    library = JavaScriptLibraryController.Instance.GetLibrary(l => l.Version.Major >= version.Major);
                    if (library != null)
                    {
                        AddItemRequest(library.JavaScriptLibraryID);
                    }
                    isProcessed = true;
                    break;
                case SpecificVersion.LatestMinor:
                    library = JavaScriptLibraryController.Instance.GetLibrary(l => l.Version.Minor >= version.Minor);
                    if (library != null)
                    {
                        AddItemRequest(library.JavaScriptLibraryID);
                    }
                    isProcessed = true;
                    break;
            }
            if (isProcessed == false)
            {
                //this should only occur if packages are incorrect or a RequestRegistration call has a typo
                Logger.TraceFormat("Missing specific version library - {0},{1},{2}", jsname, version, specific);
            }
        }

        /// <summary>
        ///     method is called once per page event cycle and will
        ///     load all scripts requested during that page processing cycle
        /// </summary>
        /// <param name="page">reference to the current page</param>
        public static void Register(Page page)
        {
            HandlePreInstallorLegacyItemRequests(page);
            IEnumerable<string> scripts = GetScriptVersions();
            object finalScripts = GetFinalScripts(scripts);
            foreach (string item in scripts)
            {
                JavaScriptLibrary library =
                    JavaScriptLibraryController.Instance.GetLibrary(l => l.JavaScriptLibraryID.ToString() == item);
                RegisterScript(page, library.LibraryName);
            }
        }

        private static object GetFinalScripts(IEnumerable<string> scripts)
        {
            var finalScripts = new List<JavaScriptLibrary>();
            foreach (string item in scripts)
            {
                JavaScriptLibrary library =
                    JavaScriptLibraryController.Instance.GetLibrary(l => l.JavaScriptLibraryID.ToString() == item);
                JavaScriptLibrary commonLibrary = finalScripts.Find(lib => lib.LibraryName == library.LibraryName);
                if (commonLibrary != null)
                {
                    //determine previous registration for same JSL
                    if (commonLibrary.Version > library.Version)
                    {
                        //skip new library & log
                        //need to log an event
                        var objEventLog = new EventLogController();
                        objEventLog.AddLog("Javascript Libraries - post depedencies",
                            commonLibrary.Version + " : " + library.Version,
                            PortalController.GetCurrentPortalSettings(),
                            UserController.GetCurrentUserInfo().UserID,
                            EventLogController.EventLogType.SCRIPT_COLLISION);
                        string strMessage = Localization.GetString("UnverifiedUser", Localization.SharedResourceFile);
                        var page = HttpContext.Current.Handler as Page;
                        if (page != null)
                        {
                            Skin.AddPageMessage(page, "", strMessage, ModuleMessage.ModuleMessageType.YellowWarning);
                        }
                    }
                    else
                    {
                        finalScripts.Remove(commonLibrary);
                    }
                }
                finalScripts.Add(library);
            }
            return finalScripts;
        }

        private static IEnumerable<string> GetScriptVersions()
        {
            List<string> orderedScripts = (from object item in HttpContext.Current.Items.Keys
                where item.ToString().StartsWith(ScriptPreix)
                select item.ToString().Substring(4)).ToList();
            orderedScripts.Sort();
            List<string> finalScripts = orderedScripts.ToList();
            foreach (string orderedScript in orderedScripts)
            {
                //find dependencies

                JavaScriptLibrary library =
                    JavaScriptLibraryController.Instance.GetLibrary(
                        l => l.JavaScriptLibraryID.ToString() == orderedScript);
                if (library != null)
                {
                    PackageInfo package = PackageController.Instance.GetExtensionPackage(Null.NullInteger,
                        p => p.PackageID == library.PackageID);
                    if (package.Dependencies.Any())
                    {
                        foreach (PackageDependencyInfo dependency in package.Dependencies)
                        {
                            JavaScriptLibrary dependantlibrary = GetHighestVersionLibrary(dependency.PackageName);
                            if (HttpContext.Current.Items[ScriptPreix + "." + dependantlibrary.JavaScriptLibraryID] ==
                                null)
                            {
                                finalScripts.Add(dependantlibrary.JavaScriptLibraryID.ToString());
                            }
                        }
                    }
                }
            }
            return finalScripts;
        }

        #endregion

        protected JavaScript()
        {
        }

        private static void AddItemRequest(int javaScriptLibraryId)
        {
            HttpContext.Current.Items[ScriptPreix + javaScriptLibraryId] = true;
        }

        private static JavaScriptLibrary GetHighestVersionLibrary(String jsname)
        {
            IEnumerable<JavaScriptLibrary> librarys =
                JavaScriptLibraryController.Instance.GetLibraries(l => l.LibraryName == jsname)
                    .OrderByDescending(l => l.Version);
            if (librarys.Any())
            {
                //need to log an event
                var objEventLog = new EventLogController();
                objEventLog.AddLog("Javascript Libraries - request",
                    librarys.ToString(),
                    PortalController.GetCurrentPortalSettings(),
                    UserController.GetCurrentUserInfo().UserID,
                    EventLogController.EventLogType.SCRIPT_COLLISION);
            }
            return librarys.First();
        }

        private static JavaScriptLibrary GetJavascriptLibrary(String jsname)
        {
            JavaScriptLibrary library = JavaScriptLibraryController.Instance.GetLibrary(l => l.LibraryName == jsname);
            if (library == null)
            {
                //this should only occur if packages are incorrect or a RequestRegistration call has a typo
                Logger.TraceFormat("Missing Library - {0}", jsname);
                return null;
            }
            return library;
        }

        private static string GetScriptPath(JavaScriptLibrary js)
        {
            if (Host.CdnEnabled)
            {
                //cdn enabled but jsl does not have one defined
                if (!String.IsNullOrEmpty(js.CDNPath))
                {
                    return js.CDNPath;
                }
            }
            return ("~/Resources/libraries/" + js.LibraryName + "/" + js.Version + "/" + js.FileName);
        }

        private static string GetScriptLocation(JavaScriptLibrary js)
        {
            switch (js.PreferredScriptLocation)
            {
                case ScriptLocation.PageHead:
                    return "DnnPageHeaderProvider";
                case ScriptLocation.BodyBottom:
                    return "DnnFormBottomProvider";
                case ScriptLocation.BodyTop:
                    return "DnnBodyProvider";
            }

            return String.Empty;
        }

        private static void RegisterScript(Page page, string js)
        {
            JavaScriptLibrary jsl = GetJavascriptLibrary(js);
            ClientResourceManager.RegisterScript(page, GetScriptPath(jsl), jsl.PackageID + 500, GetScriptLocation(jsl));

            //workaround to support IE specific script unti we move to IE version that no longer requires this
            if (jsl.LibraryName == CommonJs.jQueryFileUpload)
            {
                ClientResourceManager.RegisterScript(page,
                    "~/Resources/Shared/Scripts/jquery/jquery.iframe-transport.js");
            }

            if (Host.CdnEnabled && !String.IsNullOrEmpty(jsl.ObjectName))
            {
                string pagePortion;
                switch (jsl.PreferredScriptLocation)
                {
                    case ScriptLocation.PageHead:

                        pagePortion = "ClientDependencyHeadJs";
                        break;
                    case ScriptLocation.BodyBottom:
                        pagePortion = "ClientResourcesFormBottom";
                        break;
                    case ScriptLocation.BodyTop:
                        pagePortion = "BodySCRIPTS";
                        break;
                    default:
                        pagePortion = "BodySCRIPTS";
                        break;
                }
                Control scriptloader = page.FindControl(pagePortion);
                var fallback = new DnnJsIncludeFallback(jsl.ObjectName,
                    VirtualPathUtility.ToAbsolute("~/Resources/libraries/" + jsl.LibraryName + "/" + jsl.Version + "/" +
                                                  jsl.FileName));
                if (scriptloader != null)
                {
                    scriptloader.Controls.Add(fallback);
                }
            }
        }

        private static void HandlePreInstallorLegacyItemRequests(Page page)
        {
            List<string> legacyScripts = (from object item in HttpContext.Current.Items.Keys
                where item.ToString().StartsWith(LegacyPrefix)
                select item.ToString().Substring(7)).ToList();
            foreach (string legacyScript in legacyScripts)
            {
                switch (legacyScript)
                {
                    case CommonJs.jQuery:
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryScriptReference(),
                            FileOrder.Js.jQuery, "DnnPageHeaderProvider");
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryMigrateScriptReference(),
                            FileOrder.Js.jQueryMigrate, "DnnPageHeaderProvider");
                        break;
                    case CommonJs.jQueryUI:
                        //register dependency
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryScriptReference(),
                            FileOrder.Js.jQuery, "DnnPageHeaderProvider");
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryMigrateScriptReference(),
                            FileOrder.Js.jQueryMigrate, "DnnPageHeaderProvider");
                        //actual jqueryui
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryUIScriptReference(),
                            FileOrder.Js.jQueryUI, "DnnPageHeaderProvider");
                        break;
                    case CommonJs.DnnPlugins:
                        //This method maybe called when Page.Form hasn't initialized yet, in that situation if needed should reference dnn js manually.
                        //such as call jQuery.RegisterDnnJQueryPlugins in Control.OnInit.
                        if (page.Form != null)
                        {
                            ClientAPI.RegisterClientReference(page, ClientAPI.ClientNamespaceReferences.dnn);
                        }

                        //register dependency
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryScriptReference(),
                            FileOrder.Js.jQuery, "DnnPageHeaderProvider");
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryMigrateScriptReference(),
                            FileOrder.Js.jQueryMigrate, "DnnPageHeaderProvider");
                        //actual jqueryui
                        ClientResourceManager.RegisterScript(page, jQuery.GetJQueryUIScriptReference(),
                            FileOrder.Js.jQueryUI, "DnnPageHeaderProvider");
                        ClientResourceManager.RegisterScript(page,
                            "~/Resources/Shared/Scripts/jquery/jquery.hoverIntent.min.js", FileOrder.Js.HoverIntent);
                        ClientResourceManager.RegisterScript(page, "~/Resources/Shared/Scripts/dnn.jquery.js");
                        break;
                    case CommonJs.jQueryFileUpload:
                        ClientResourceManager.RegisterScript(page,
                            "~/Resources/Shared/Scripts/jquery/jquery.iframe-transport.js");
                        ClientResourceManager.RegisterScript(page,
                            "~/Resources/Shared/Scripts/jquery/jquery.fileupload.js");
                        break;
                    case CommonJs.HoverIntent:
                        ClientResourceManager.RegisterScript(page,
                            "~/Resources/Shared/Scripts/jquery/jquery.hoverIntent.min.js", FileOrder.Js.HoverIntent);
                        break;
                }
            }
        }

        private static bool IsInstallationUrl()
        {
            string requestUrl = HttpContext.Current.Request.RawUrl.ToLowerInvariant();
            return requestUrl.Contains("/install.aspx") || requestUrl.Contains("/installwizard.aspx");
        }

        #region Legacy methods and preinstall support

        private const string jQueryDebugFile = "~/Resources/Shared/Scripts/jquery/jquery.js";
        private const string jQueryMinFile = "~/Resources/Shared/Scripts/jquery/jquery.min.js";
        private const string jQueryMigrateDebugFile = "~/Resources/Shared/Scripts/jquery/jquery-migrate.js";
        private const string jQueryMigrateMinFile = "~/Resources/Shared/Scripts/jquery/jquery-migrate.min.js";
        private const string jQueryUIDebugFile = "~/Resources/Shared/Scripts/jquery/jquery-ui.js";
        private const string jQueryUIMinFile = "~/Resources/Shared/Scripts/jquery/jquery-ui.min.js";
        private const string jQueryHoverIntentFile = "~/Resources/Shared/Scripts/jquery/jquery.hoverIntent.min.js";

        private static void RegisterJQuery(Page page)
        {
            ClientResourceManager.RegisterScript(page, GetJQueryScriptReference(), FileOrder.Js.jQuery,
                "DnnPageHeaderProvider");
            ClientResourceManager.RegisterScript(page, GetJQueryMigrateScriptReference(), FileOrder.Js.jQueryMigrate,
                "DnnPageHeaderProvider");
        }


        private static void RegisterJQueryUI(Page page)
        {
            RegisterJQuery(page);
            ClientResourceManager.RegisterScript(page, GetJQueryUIScriptReference(), FileOrder.Js.jQueryUI,
                "DnnPageHeaderProvider");
        }

        private static void RegisterDnnJQueryPlugins(Page page)
        {
            //This method maybe called when Page.Form hasn't initialized yet, in that situation if needed should reference dnn js manually.
            //such as call jQuery.RegisterDnnJQueryPlugins in Control.OnInit.
            if (page.Form != null)
            {
                ClientAPI.RegisterClientReference(page, ClientAPI.ClientNamespaceReferences.dnn);
            }

            RegisterJQueryUI(page);
            RegisterHoverIntent(page);
            ClientResourceManager.RegisterScript(page, "~/Resources/Shared/Scripts/dnn.jquery.js");
        }

        private static void RegisterHoverIntent(Page page)
        {
            ClientResourceManager.RegisterScript(page, jQueryHoverIntentFile, FileOrder.Js.HoverIntent);
        }

        private static void RegisterFileUpload(Page page)
        {
            ClientResourceManager.RegisterScript(page, "~/Resources/Shared/Scripts/jquery/jquery.iframe-transport.js");
            ClientResourceManager.RegisterScript(page, "~/Resources/Shared/Scripts/jquery/jquery.fileupload.js");
        }

        public static string JQueryUIFile(bool getMinFile)
        {
            string jfile = jQueryUIDebugFile;
            if (getMinFile)
            {
                jfile = jQueryUIMinFile;
            }
            return jfile;
        }

        public static string GetJQueryScriptReference()
        {
            string scriptsrc = jQuery.HostedUrl;
            if (!jQuery.UseHostedScript)
            {
                scriptsrc = jQuery.JQueryFile(!jQuery.UseDebugScript);
            }
            return scriptsrc;
        }

        private static string GetJQueryMigrateScriptReference()
        {
            string scriptsrc = jQuery.HostedMigrateUrl;
            if (!jQuery.UseHostedScript || string.IsNullOrEmpty(scriptsrc))
            {
                scriptsrc = jQuery.JQueryMigrateFile(!jQuery.UseDebugScript);
            }
            return scriptsrc;
        }

        private static string GetJQueryUIScriptReference()
        {
            string scriptsrc = jQuery.HostedUIUrl;
            if (!jQuery.UseHostedScript)
            {
                scriptsrc = JQueryUIFile(!jQuery.UseDebugScript);
            }
            return scriptsrc;
        }

        #endregion
    }
}