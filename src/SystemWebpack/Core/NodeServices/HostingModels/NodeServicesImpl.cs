// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : NodeServicesImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************

using System.Web.Hosting;

namespace SystemWebpack.Core.NodeServices.HostingModels {
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Default implementation of INodeServices. This is the primary API surface through which developers
    /// make use of this package. It provides simple "InvokeAsync" methods that dispatch calls to the
    /// correct Node instance, creating and destroying those instances as needed.
    ///
    /// If a Node instance dies (or none was yet created), this class takes care of creating a new one.
    /// If a Node instance signals that it needs to be restarted (e.g., because a file changed), then this
    /// class will create a new instance and dispatch future calls to it, while keeping the old instance
    /// alive for a defined period so that any in-flight RPC calls can complete. This latter feature is
    /// analogous to the "connection draining" feature implemented by HTTP load balancers.
    /// </summary>
    /// <seealso cref="INodeServices" />
    internal class NodeServicesImpl : INodeServices, IRegisteredObject {
        private static TimeSpan ConnectionDrainingTimespan = TimeSpan.FromSeconds(15);
        private Func<INodeInstance> _nodeInstanceFactory;
        private INodeInstance _currentNodeInstance;
        private object _currentNodeInstanceAccessLock = new object();
        private Exception _instanceDelayedDisposalException;

        internal NodeServicesImpl(Func<INodeInstance> nodeInstanceFactory) {
            this._nodeInstanceFactory = nodeInstanceFactory;

            HostingEnvironment.RegisterObject(this);
        }

        public Task<T> InvokeAsync<T>(string moduleName, params object[] args) {
            return this.InvokeExportAsync<T>(moduleName, null, args);
        }

        public Task<T> InvokeAsync<T>(CancellationToken cancellationToken, string moduleName, params object[] args) {
            return this.InvokeExportAsync<T>(cancellationToken, moduleName, null, args);
        }

        public Task<T> InvokeExportAsync<T>(string moduleName, string exportedFunctionName, params object[] args) {
            return this.InvokeExportWithPossibleRetryAsync<T>(moduleName, exportedFunctionName, args, /* allowRetry */ true, CancellationToken.None);
        }

        public Task<T> InvokeExportAsync<T>(CancellationToken cancellationToken, string moduleName, string exportedFunctionName, params object[] args) {
            return this.InvokeExportWithPossibleRetryAsync<T>(moduleName, exportedFunctionName, args, /* allowRetry */ true, cancellationToken);
        }

        private async Task<T> InvokeExportWithPossibleRetryAsync<T>(string moduleName, string exportedFunctionName, object[] args, bool allowRetry, CancellationToken cancellationToken) {
            this.ThrowAnyOutstandingDelayedDisposalException();
            var nodeInstance = this.GetOrCreateCurrentNodeInstance();

            try {
                return await nodeInstance.InvokeExportAsync<T>(cancellationToken, moduleName, exportedFunctionName, args);
            } catch (NodeInvocationException ex) {
                // If the Node instance can't complete the invocation because it needs to restart (e.g., because the underlying
                // Node process has exited, or a file it depends on has changed), then we make one attempt to restart transparently.
                if (allowRetry && ex.NodeInstanceUnavailable) {
                    // Perform the retry after clearing away the old instance
                    // Since we disposal is delayed even though the node instance is replaced immediately, this produces the
                    // "connection draining" feature whereby in-flight RPC calls are given a certain period to complete.
                    lock (this._currentNodeInstanceAccessLock) {
                        if (this._currentNodeInstance == nodeInstance) {
                            var disposalDelay = ex.AllowConnectionDraining ? ConnectionDrainingTimespan : TimeSpan.Zero;
                            this.DisposeNodeInstance(this._currentNodeInstance, disposalDelay);
                            this._currentNodeInstance = null;
                        }
                    }

                    // One the next call, don't allow retries, because we could get into an infinite retry loop, or a long retry
                    // loop that masks an underlying problem. A newly-created Node instance should be able to accept invocations,
                    // or something more serious must be wrong.
                    return await this.InvokeExportWithPossibleRetryAsync<T>(moduleName, exportedFunctionName, args, /* allowRetry */ false, cancellationToken);
                } else {
                    throw;
                }
            }
        }

        public void Dispose() {
            lock (this._currentNodeInstanceAccessLock) {
                if (this._currentNodeInstance != null) {
                    this.DisposeNodeInstance(this._currentNodeInstance, delay: TimeSpan.Zero);
                    this._currentNodeInstance = null;
                }

                HostingEnvironment.UnregisterObject(this);
            }
        }

        private void DisposeNodeInstance(INodeInstance nodeInstance, TimeSpan delay) {
            if (delay == TimeSpan.Zero) {
                nodeInstance.Dispose();
            } else {
                Task.Run(async () => {
                    try {
                        await Task.Delay(delay);
                        nodeInstance.Dispose();
                    } catch (Exception ex) {
                        // Nothing's waiting for the delayed disposal task, so any exceptions in it would
                        // by default just get ignored. To make these discoverable, capture them here so
                        // they can be rethrown to the next caller to InvokeExportAsync.
                        this._instanceDelayedDisposalException = ex;
                    }
                });
            }
        }

        private void ThrowAnyOutstandingDelayedDisposalException() {
            if (this._instanceDelayedDisposalException != null) {
                var ex = this._instanceDelayedDisposalException;
                this._instanceDelayedDisposalException = null;
                throw new AggregateException(
                    "A previous attempt to dispose a Node instance failed. See InnerException for details.",
                    ex);
            }
        }

        private INodeInstance GetOrCreateCurrentNodeInstance() {
            var instance = this._currentNodeInstance;
            if (instance == null) {
                lock (this._currentNodeInstanceAccessLock) {
                    instance = this._currentNodeInstance;
                    if (instance == null) {
                        instance = this._currentNodeInstance = this.CreateNewNodeInstance();
                    }
                }
            }

            return instance;
        }

        private INodeInstance CreateNewNodeInstance() {
            return this._nodeInstanceFactory();
        }

        public void Stop(bool immediate) {
            this.Dispose();
        }
    }
}
