﻿// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : NodeServicesOptionsExtensions.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core.NodeServices.HostingModels {
    /// <summary>
    /// Extension methods that help with populating a <see cref="NodeServicesOptions"/> object.
    /// </summary>
    internal static class NodeServicesOptionsExtensions {
        /// <summary>
        /// Configures the <see cref="INodeServices"/> service so that it will use out-of-process
        /// Node.js instances and perform RPC calls over HTTP.
        /// </summary>
        public static void UseHttpHosting(this NodeServicesOptions options) {
            options.NodeInstanceFactory = () => new HttpNodeInstance(options);
        }
    }
}