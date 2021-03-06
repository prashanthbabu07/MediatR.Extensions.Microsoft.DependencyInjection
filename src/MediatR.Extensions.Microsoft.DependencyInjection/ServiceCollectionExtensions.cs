﻿namespace MediatR
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Extensions.DependencyInjection;
    using Pipeline;

    /// <summary>
    /// Extensions to scan for MediatR handlers and registers them.
    /// - Scans for any handler interface implementations and registers them as <see cref="ServiceLifetime.Transient"/>
    /// - Scans for any <see cref="IRequestPreProcessor{TRequest}"/> and <see cref="IRequestPostProcessor{TRequest,TResponse}"/> implementations and registers them as scoped instances
    /// Registers <see cref="SingleInstanceFactory"/>, <see cref="MultiInstanceFactory"/> and <see cref="IMediator"/> as scoped instances
    /// After calling AddMediatR you can use the container to resolve an <see cref="IMediator"/> instance.
    /// This does not scan for any <see cref="IPipelineBehavior{TRequest,TResponse}"/> instances including <see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/> and <see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/>.
    /// To register behaviors, use the <see cref="ServiceCollectionServiceExtensions.AddTransient(IServiceCollection,Type,Type)"/> with the open generic or closed generic types.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers handlers and the mediator types from <see cref="AppDomain.CurrentDomain"/>.
        /// </summary>
        /// <param name="services">Service collection</param>
        public static void AddMediatR(this IServiceCollection services)
        {
            services.AddMediatR(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic));
        }

        /// <summary>
        /// Registers handlers and mediator types from the specified assemblies
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="assemblies">Assemblies to scan</param>
        public static void AddMediatR(this IServiceCollection services, params Assembly[] assemblies)
        {
            AddRequiredServices(services);

            AddMediatRClasses(services, assemblies);
        }

        /// <summary>
        /// Registers handlers and mediator types from the specified assemblies
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="assemblies">Assemblies to scan</param>
        public static void AddMediatR(this IServiceCollection services, IEnumerable<Assembly> assemblies)
        {
            AddRequiredServices(services);

            AddMediatRClasses(services, assemblies);
        }

        /// <summary>
        /// Registers handlers and mediator types from the assemblies that contain the specified types
        /// </summary>
        /// <param name="services"></param>
        /// <param name="handlerAssemblyMarkerTypes"></param>
        public static void AddMediatR(this IServiceCollection services, params Type[] handlerAssemblyMarkerTypes)
        {
            AddRequiredServices(services);

            AddMediatRClasses(services, handlerAssemblyMarkerTypes.Select(t => t.GetTypeInfo().Assembly));
        }

        /// <summary>
        /// Registers handlers and mediator types from the assemblies that contain the specified types
        /// </summary>
        /// <param name="services"></param>
        /// <param name="handlerAssemblyMarkerTypes"></param>
        public static void AddMediatR(this IServiceCollection services, IEnumerable<Type> handlerAssemblyMarkerTypes)
        {
            AddRequiredServices(services);
            AddMediatRClasses(services, handlerAssemblyMarkerTypes.Select(t => t.GetTypeInfo().Assembly));
        }


        private static void AddMediatRClasses(IServiceCollection services, IEnumerable<Assembly> assembliesToScan)
        {
            assembliesToScan = assembliesToScan as Assembly[] ?? assembliesToScan.ToArray();

            var openInterfaces = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(IRequestHandler<>),
                typeof(IAsyncRequestHandler<,>),
                typeof(IAsyncRequestHandler<>),
                typeof(ICancellableAsyncRequestHandler<,>),
                typeof(ICancellableAsyncRequestHandler<>),
                typeof(INotificationHandler<>),
                typeof(IAsyncNotificationHandler<>),
                typeof(ICancellableAsyncNotificationHandler<>),
            };
            foreach (var openInterface in openInterfaces)
            {
                var concretions = new List<Type>();
                var interfaces = new List<Type>();

                foreach (var type in assembliesToScan.SelectMany(a => a.ExportedTypes))
                {
                    IEnumerable<Type> interfaceTypes = type.FindInterfacesThatClose(openInterface).ToArray();
                    if (!interfaceTypes.Any()) continue;

                    if (type.IsConcrete())
                    {
                        concretions.Add(type);
                    }

                    foreach (Type interfaceType in interfaceTypes)
                    {
                        interfaces.Fill(interfaceType);
                    }
                }

                foreach (var @interface in interfaces)
                {
                    var exactMatches = concretions.Where(t => t.CanBeCastTo(@interface)).ToArray();

                    foreach (var exactMatch in exactMatches)
                    {
                        services.AddTransient(@interface, exactMatch);
                    }

                    if (!@interface.IsOpenGeneric())
                    {
                        AddConcretionsThatCouldBeClosed(@interface, concretions, services);
                    }
                }
            }

            var multiOpenInterfaces = new[]
            {
                typeof(IRequestPreProcessor<>),
                typeof(IRequestPostProcessor<,>)
            };

            foreach (var multiOpenInterface in multiOpenInterfaces)
            {
                var concretions = new List<Type>();

                foreach (var type in assembliesToScan.SelectMany(a => a.ExportedTypes))
                {
                    IEnumerable<Type> interfaceTypes = type.FindInterfacesThatClose(multiOpenInterface).ToArray();
                    if (!interfaceTypes.Any()) continue;

                    if (type.IsConcrete())
                    {
                        concretions.Add(type);
                    }
                }

                foreach (var concretion in concretions)
                {
                    services.AddTransient(multiOpenInterface, concretion);
                }
            }
        }

        private static void AddConcretionsThatCouldBeClosed(Type @interface, List<Type> concretions, IServiceCollection services)
        {
            foreach (var type in concretions.Where(x => x.IsOpenGeneric())
                .Where(x => x.CouldCloseTo(@interface)))
            {
                try
                {
                    services.AddTransient(@interface, type.MakeGenericType(@interface.GenericTypeArguments));
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool CouldCloseTo(this Type openConcretion, Type closedInterface)
        {
            var openInterface = closedInterface.GetGenericTypeDefinition();
            var arguments = closedInterface.GenericTypeArguments;

            var concreteArguments = openConcretion.GenericTypeArguments;
            return arguments.Length == concreteArguments.Length && openConcretion.CanBeCastTo(openInterface);
        }

        private static bool CanBeCastTo(this Type pluggedType, Type pluginType)
        {
            if (pluggedType == null) return false;

            if (pluggedType == pluginType) return true;

            return pluginType.GetTypeInfo().IsAssignableFrom(pluggedType.GetTypeInfo());
        }

        public static bool IsOpenGeneric(this Type type)
        {
            return type.GetTypeInfo().IsGenericTypeDefinition || type.GetTypeInfo().ContainsGenericParameters;
        }

        private static IEnumerable<Type> FindInterfacesThatClose(this Type pluggedType, Type templateType)
        {
            if (!pluggedType.IsConcrete()) yield break;

            if (templateType.GetTypeInfo().IsInterface)
            {
                foreach (
                    var interfaceType in
                        pluggedType.GetTypeInfo().ImplementedInterfaces
                            .Where(type => type.GetTypeInfo().IsGenericType && (type.GetGenericTypeDefinition() == templateType)))
                {
                    yield return interfaceType;
                }
            }
            else if (pluggedType.GetTypeInfo().BaseType.GetTypeInfo().IsGenericType &&
                     (pluggedType.GetTypeInfo().BaseType.GetGenericTypeDefinition() == templateType))
            {
                yield return pluggedType.GetTypeInfo().BaseType;
            }

            if (pluggedType == typeof(object)) yield break;
            if (pluggedType.GetTypeInfo().BaseType == typeof(object)) yield break;

            foreach (var interfaceType in FindInterfacesThatClose(pluggedType.GetTypeInfo().BaseType, templateType))
            {
                yield return interfaceType;
            }
        }

        private static bool IsConcrete(this Type type)
        {
            return !type.GetTypeInfo().IsAbstract && !type.GetTypeInfo().IsInterface;
        }

        private static void Fill<T>(this IList<T> list, T value)
        {
            if (list.Contains(value)) return;
            list.Add(value);
        }

        private static void AddRequiredServices(IServiceCollection services)
        {
            services.AddScoped<SingleInstanceFactory>(p => t => p.GetService(t));
            services.AddScoped<MultiInstanceFactory>(p => t => p.GetServices(t));
            services.AddScoped<IMediator, Mediator>();
        }
    }
}