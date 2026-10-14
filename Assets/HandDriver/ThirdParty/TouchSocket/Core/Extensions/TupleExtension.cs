using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TouchSocket.Core
{
    /// <summary>
    /// 元组扩展
    /// </summary>
    public static class TupleExtension
    {
        /// <summary>
        /// 获取元组的名称列表。
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public static IEnumerable<string> GetTupleElementNames(this ParameterInfo parameter)
        {
            return GetTransformNames(parameter.GetCustomAttribute(Type.GetType("System.Runtime.CompilerServices.TupleElementNamesAttribute")));
        }

        /// <summary>
        /// 获取元组的名称列表。
        /// </summary>
        /// <param name="memberInfo"></param>
        /// <returns></returns>
        public static IEnumerable<string> GetTupleElementNames(this MemberInfo  memberInfo)
        {
            return GetTransformNames(memberInfo.GetCustomAttribute(Type.GetType("System.Runtime.CompilerServices.TupleElementNamesAttribute")));
        }

        private static IEnumerable<string> GetTransformNames(Attribute attribute)
        {
            return attribute?.GetType().GetProperty("TransformNames")?.GetValue(attribute) as IEnumerable<string>;
        }
    }
}
