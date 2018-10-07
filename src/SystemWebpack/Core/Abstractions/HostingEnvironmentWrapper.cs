// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : HostingEnvironmentWrapper.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.Abstractions {
    using System;
    using System.Collections.Specialized;
    using System.Web.Configuration;
    using System.Web.Hosting;

    internal interface IHostingEnvironment {
        string ContentRootPath { get;}

        string NodeEnvironment { get; }
    }

    internal class HostingEnvironmentWrapper : IHostingEnvironment {
        private readonly Lazy<NameValueCollection> _appSettings = new Lazy<NameValueCollection>(() => WebConfigurationManager.AppSettings, true);

        public string ContentRootPath => HostingEnvironment.MapPath("~/");

        public string NodeEnvironment => this._appSettings.Value[ConfigurationKeys.NodeEnvironment];
    }

    internal static class HostingEnvironmentExtensions {
        public static bool IsDevelopment(this IHostingEnvironment hostingEnvironment) {
            return !String.Equals(hostingEnvironment.NodeEnvironment, "production", StringComparison.OrdinalIgnoreCase);
        }
    }
}