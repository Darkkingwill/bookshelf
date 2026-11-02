using System;
using System.Linq;
using System.Linq.Expressions;
using Dapper;

namespace NzbDrone.Core.Datastore
{
    public abstract class WhereBuilder : ExpressionVisitor
    {
        public DynamicParameters Parameters { get; protected set; }

        // Static Contains(source, item) is Enumerable.Contains for a list. Since C# 14 it is MemoryExtensions.Contains(span, item, comparer: null)
        // for an array, which is wrapped in an implicit conversion to a span; get the array back.
        protected static bool TryGetStaticContainsOperands(MethodCallExpression body, out Expression list, out Expression item)
        {
            list = null;
            item = null;

            var arguments = body.Arguments;

            if (body.Method.DeclaringType == typeof(Enumerable) && arguments.Count == 2)
            {
                list = arguments[0];
                item = arguments[1];
                return true;
            }

            if (body.Method.DeclaringType == typeof(MemoryExtensions) &&
                (arguments.Count == 2 || (arguments.Count == 3 && arguments[2] is ConstantExpression { Value: null })))
            {
                list = arguments[0] switch
                {
                    MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } conversion when conversion.Arguments[0].Type.IsArray => conversion.Arguments[0],
                    UnaryExpression { NodeType: ExpressionType.Convert } cast when cast.Operand.Type.IsArray => cast.Operand,
                    var other => other
                };
                item = arguments[1];
                return true;
            }

            return false;
        }
    }
}
