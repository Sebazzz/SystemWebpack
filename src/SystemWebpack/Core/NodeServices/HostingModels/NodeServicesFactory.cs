// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : NodeServicesFactory.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core.NodeServices.HostingModels {
    using System;

    /// <summary>
    ///     Supplies INodeServices instances.
    /// </summary>
    internal static class NodeServicesFactory {
        /// <summary>
        ///     Create an <see cref="INodeServices" /> instance according to the supplied options.
        /// </summary>
        /// <param name="options">Options for creating the <see cref="INodeServices" /> instance.</param>
        /// <returns>An <see cref="INodeServices" /> instance.</returns>
        public static INodeServices CreateNodeServices(NodeServicesOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));

            return new NodeServicesImpl(options.NodeInstanceFactory);
        }
    }
}