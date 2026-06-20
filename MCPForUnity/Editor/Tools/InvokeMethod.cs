using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("invoke_method", AutoRegister = false)]
    public static class InvokeMethod
    {
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var methodName = p.Get("method");
            if (string.IsNullOrWhiteSpace(methodName))
                return new ErrorResponse("'method' parameter is required.");

            var argsToken = p.GetRaw("args");
            var args = argsToken is JArray ja ? ja : null;

            // Determine mode: instance method on component vs static method on type
            string typeName = p.Get("type");
            string targetStr = p.Get("target");
            string componentName = p.Get("component");

            if (!string.IsNullOrEmpty(typeName))
                return HandleStaticInvocation(typeName, methodName, args);

            if (!string.IsNullOrEmpty(targetStr) || !string.IsNullOrEmpty(componentName))
                return HandleInstanceInvocation(@params, p, methodName, args);

            return new ErrorResponse(
                "'target'+'component' (for instance methods) or 'type' (for static methods) is required.");
        }

        private static object HandleInstanceInvocation(JObject @params, ToolParams p, string methodName, JArray args)
        {
            var targetToken = p.GetRaw("target");
            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for instance method invocation.");

            string componentName = p.Get("component");
            if (string.IsNullOrWhiteSpace(componentName))
                return new ErrorResponse("'component' parameter is required for instance method invocation.");

            // Resolve GameObject (by instance_id, path, or name)
            var go = ResolveGameObject(targetToken);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {targetToken}");

            // Resolve component
            if (!ComponentResolver.TryResolve(componentName, out var componentType, out var resolveError))
                return new ErrorResponse($"Component type not found: {componentName}. {resolveError}");

            var component = go.GetComponent(componentType);
            if (component == null)
                return new ErrorResponse(
                    $"Component '{componentType.Name}' not found on '{go.name}'. " +
                    $"Available: {string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}");

            // Find and invoke method
            return FindAndInvoke(componentType, methodName, args, component, InstanceFlags);
        }

        private static object HandleStaticInvocation(string typeName, string methodName, JArray args)
        {
            if (!UnityTypeResolver.TryResolve(typeName, out var type, out var resolveError))
                return new ErrorResponse($"Type not found: {typeName}. {resolveError}");

            return FindAndInvoke(type, methodName, args, null, StaticFlags);
        }

        private static object FindAndInvoke(Type type, string methodName, JArray args, object instance, BindingFlags flags)
        {
            int argCount = args?.Count ?? 0;

            // Get all methods with matching name
            var candidates = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                var available = type.GetMethods(flags)
                    .Where(m => !m.IsSpecialName) // skip property getters/setters
                    .Select(m => FormatMethodSignature(m))
                    .Distinct()
                    .OrderBy(s => s)
                    .Take(30)
                    .ToList();

                return new ErrorResponse(
                    $"Method '{methodName}' not found on {type.Name}.",
                    new { availableMethods = available });
            }

            // Filter by argument count
            var byArgCount = candidates
                .Where(m =>
                {
                    var ps = m.GetParameters();
                    int required = ps.Count(param => !param.HasDefaultValue);
                    int total = ps.Length;
                    return argCount >= required && argCount <= total;
                })
                .ToList();

            if (byArgCount.Count == 0)
            {
                return new ErrorResponse(
                    $"No overload of '{methodName}' on {type.Name} accepts {argCount} argument(s).",
                    new
                    {
                        candidates = candidates.Select(FormatMethodSignature).ToList()
                    });
            }

            // Try each candidate until one works
            Exception lastError = null;
            foreach (var method in byArgCount)
            {
                try
                {
                    var convertedArgs = ConvertArguments(method, args);
                    var result = method.Invoke(instance, convertedArgs);
                    return SerializeResult(method, result);
                }
                catch (TargetInvocationException tie)
                {
                    // The invoked method itself threw — report that directly
                    var inner = tie.InnerException ?? tie;
                    return new ErrorResponse(
                        $"Method '{methodName}' threw an exception: {inner.GetType().Name}: {inner.Message}",
                        new { exceptionType = inner.GetType().FullName, stackTrace = inner.StackTrace });
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    // Argument conversion failed for this overload, try next
                }
            }

            return new ErrorResponse(
                $"Could not invoke '{methodName}' on {type.Name}: {lastError?.Message}",
                new { candidates = byArgCount.Select(FormatMethodSignature).ToList() });
        }

        private static object[] ConvertArguments(MethodInfo method, JArray args)
        {
            var parameters = method.GetParameters();
            var result = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                if (args != null && i < args.Count)
                {
                    result[i] = PropertyConversion.ConvertToType(args[i], parameters[i].ParameterType);
                }
                else if (parameters[i].HasDefaultValue)
                {
                    result[i] = parameters[i].DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Missing required argument '{parameters[i].Name}' at position {i}.");
                }
            }

            return result;
        }

        private static object SerializeResult(MethodInfo method, object result)
        {
            if (method.ReturnType == typeof(void))
            {
                return new SuccessResponse($"Method '{method.Name}' invoked successfully (void).",
                    new { returnValue = (object)null, returnType = "void" });
            }

            object serialized;
            try
            {
                var token = JToken.FromObject(result ?? "null", GameObjectSerializer.OutputSerializer);
                serialized = GameObjectSerializer.ConvertJTokenToPlainObject(token);
            }
            catch
            {
                serialized = result?.ToString();
            }

            return new SuccessResponse($"Method '{method.Name}' returned {method.ReturnType.Name}.",
                new
                {
                    returnValue = serialized,
                    returnType = method.ReturnType.FullName
                });
        }

        private static string FormatMethodSignature(MethodInfo m)
        {
            var ps = m.GetParameters();
            var paramStr = string.Join(", ", ps.Select(param =>
            {
                var prefix = param.HasDefaultValue ? "[optional] " : "";
                return $"{prefix}{param.ParameterType.Name} {param.Name}";
            }));
            return $"{m.ReturnType.Name} {m.Name}({paramStr})";
        }

        private static GameObject ResolveGameObject(JToken targetToken)
        {
            if (targetToken == null) return null;

            // By instance ID
            if (targetToken.Type == JTokenType.Integer || int.TryParse(targetToken.ToString(), out _))
            {
                int id = targetToken.Type == JTokenType.Integer
                    ? targetToken.Value<int>()
                    : int.Parse(targetToken.ToString());
                return GameObjectLookup.FindById(id);
            }

            string targetStr = targetToken.ToString();

            // By path (contains /)
            if (targetStr.Contains("/"))
            {
                // Drop a leading slash: by_path with includeInactive=true compares
                // against GetGameObjectPath (no leading slash), so an absolute-style
                // path like "/Parent/Child" would otherwise never match.
                var path = targetStr.StartsWith("/") ? targetStr.Substring(1) : targetStr;
                return GameObjectLookup.FindByTarget(new JValue(path), "by_path", true);
            }

            // By name
            return GameObjectLookup.FindByTarget(targetToken, "by_name", true);
        }
    }
}
