using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp.Cdp.Messaging;
using PuppeteerSharp.Helpers.Json;

namespace PuppeteerSharp
{
    internal static class BindingUtils
    {
        internal static string PageBindingInitString(string type, string name)
            => EvaluationString(
                @"function addPageBinding(type, name) {
                    // This is the CDP binding.
                    const callCdp = globalThis[name];

                    // Depending on the frame loading state either Runtime.evaluate or
                    // Page.addScriptToEvaluateOnNewDocument might succeed. Let's check that we
                    // don't re-wrap Puppeteer's binding.
                    if (callCdp[Symbol.toStringTag] === 'PuppeteerBinding') {
                      return;
                    }

                    // We replace the CDP binding with a Puppeteer binding.
                    Object.assign(globalThis, {
                    [name](...args) {
                      // This is the Puppeteer binding.
                      const callPuppeteer = globalThis[name];
                      callPuppeteer.args ??= new Map();
                      callPuppeteer.callbacks ??= new Map();

                      const seq = (callPuppeteer.lastSeq ?? 0) + 1;
                      callPuppeteer.lastSeq = seq;
                      callPuppeteer.args.set(seq, args);

                      callCdp(
                        JSON.stringify({
                            type,
                            name,
                            seq,
                            args,
                            isTrivial: !args.some(value => {
                            return value instanceof Node;
                            }),
                        })
                      );

                      return new Promise((resolve, reject) => {
                        callPuppeteer.callbacks.set(seq, {
                          resolve(value) {
                            callPuppeteer.args.delete(seq);
                            resolve(value);
                          },
                          reject(value) {
                            callPuppeteer.args.delete(seq);
                            reject(value);
                          },
                        });
                      });
                    },
                  });
                  globalThis[name][Symbol.toStringTag] = 'PuppeteerBinding';
                }",
                type,
                name);

        internal static string EvaluationString(string fun, params object[] args)
        {
            return $"({fun})({string.Join(",", args.Select(SerializeArgument))})";

            static string SerializeArgument(object arg)
            {
                return arg == null
                    ? "undefined"
                    : JsonSerializer.Serialize(arg, JsonHelper.DefaultJsonSerializerSettings.Value);
            }
        }

        internal static async Task ExecuteBindingAsync(ExecutionContext context, BindingCalledResponse e, ConcurrentDictionary<string, Binding> pageBindings)
        {
            var binding = pageBindings[e.BindingPayload.Name];
            var methodParams = binding.Function.Method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            var args = e.BindingPayload.Args.Select((token, i) => token.ToObject(methodParams[i])).Cast<object>().ToArray();

            await binding.RunAsync(context, e.BindingPayload.Seq, args, e.BindingPayload.IsTrivial).ConfigureAwait(false);
        }

        internal static async Task<object> OldExecuteBindingAsync(Delegate binding, object[] rawArgs)
        {
            const string taskResultPropertyName = "Result";
            object result;
            var methodParams = binding.Method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            var args = rawArgs.Select((arg, i) => arg is JsonElement token ? token.ToObject(methodParams[i]) : arg).ToArray();

            result = binding.DynamicInvoke(args);
            if (result is Task taskResult)
            {
                await taskResult.ConfigureAwait(false);

                if (taskResult.GetType().IsGenericType)
                {
                    // the task is already awaited and therefore the call to property Result will not deadlock
                    result = taskResult.GetType().GetProperty(taskResultPropertyName).GetValue(taskResult);
                }
            }

            return result;
        }

        internal static async Task<object> ExecuteBindingAsync(Delegate binding, object[] rawArgs)
        {
            // 执行绑定委托
            var result = binding.DynamicInvoke(rawArgs);

            if (result is Task taskResult)
            {
                // 等待任务完成
                await taskResult.ConfigureAwait(false);

                // 检查是否为泛型 Task<T>
                if (taskResult.GetType().IsGenericType)
                {
                    try
                    {
                        // 获取泛型参数类型（例如 string）
                        var resultType = taskResult.GetType().GetGenericArguments()[0];

                        // 创建 TaskResultAccessor<T> 的类型（例如 TaskResultAccessor<string>）
                        var accessorType = typeof(TaskResultAccessor<>).MakeGenericType(resultType);

                        // 实例化包装类
                        var accessor = Activator.CreateInstance(accessorType, taskResult);

                        // 获取 GetResult 方法并调用
                        var getResultMethod = accessorType.GetMethod("GetResult");
                        result = getResultMethod.Invoke(accessor, null);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
                else
                {
                    result = null; // 处理非泛型 Task 的情况
                }
            }

            return result;
        }
    }
}
