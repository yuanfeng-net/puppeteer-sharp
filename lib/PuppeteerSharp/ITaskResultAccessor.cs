using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace PuppeteerSharp
{
    /// <summary>
    /// ITaskResultAccessorTest.
    /// </summary>
#pragma warning disable SA1618 // Generic type parameters should be documented
    public interface ITaskResultAccessor<T>
#pragma warning restore SA1618 // Generic type parameters should be documented
    {
        /// <summary>
        /// Get.
        /// </summary>
        /// <returns>T.</returns>
        T GetResult();
    }

    /// <inheritdoc/>
    public class TaskResultAccessor<T> : ITaskResultAccessor<T>
    {
        private readonly Task<T> _task;

#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
#pragma warning disable SA1600 // Elements should be documented
        public TaskResultAccessor(Task<T> task)
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
        }

        /// <inheritdoc/>
        public T GetResult()
        {
            return _task.Result; // 直接访问 Result 属性
        }
    }
}
