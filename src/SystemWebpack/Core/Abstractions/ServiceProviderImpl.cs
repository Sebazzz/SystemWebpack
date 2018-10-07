// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : ServiceProviderImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.Abstractions {
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Design;

    internal class ServiceProviderImpl : IServiceProvider {
        private readonly Dictionary<Type, object> _services;

        public ServiceProviderImpl() {
            this._services = new Dictionary<Type, object>();
        }

        public void RegisterService(Type serviceType, object implementation) {
            try {
                this._services.Add(serviceType, implementation);
            }
            catch (ArgumentException ex) {
                throw new ArgumentException($"Service of type {serviceType} already registered earlier", ex);
            }
        }

        public object GetService(Type serviceType) {
            if (!this._services.TryGetValue(serviceType, out object serviceImpl)) {
                throw new ArgumentException($"Unknown service type: {serviceType}");
            }

            return serviceImpl;
        }
    }

    internal static class ServiceProviderExtensions {
        public static T GetService<T>(this IServiceProvider serviceProvider) where T : class {
            return (T) serviceProvider.GetService(typeof(T));
        }
    }
}