using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using FastExpressionCompiler;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Compiler;

internal sealed class ExpressionToSqlVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly ParameterBag _params;
    private readonly EntityMapRegistry _registry;
    private readonly AliasRegistry _aliases;
    private readonly bool _unaliased;

    public string Sql => _sql.ToString();

    public ExpressionToSqlVisitor(ParameterBag parameters, EntityMapRegistry registry, AliasRegistry aliases, bool unaliased = false)
    {
        _params = parameters;
        _registry = registry;
        _aliases = aliases;
        _unaliased = unaliased;
    }

    public string Compile(LambdaExpression expression)
    {
        _sql.Clear();
        Visit(expression.Body);
        return _sql.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.Right is ConstantExpression { Value: null } &&
            node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            Visit(node.Left);
            _sql.Append(node.NodeType == ExpressionType.Equal ? " IS NULL" : " IS NOT NULL");
            return node;
        }

        var op = MapOperator(node.NodeType);
        if (op is " AND " or " OR ")
        {
            _sql.Append('(');
            Visit(node.Left);
            _sql.Append(op);
            Visit(node.Right);
            _sql.Append(')');
        }
        else
        {
            Visit(node.Left);
            _sql.Append(op);
            Visit(node.Right);
        }
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _sql.Append("NOT (");
            Visit(node.Operand);
            _sql.Append(')');
            return node;
        }
        if (node.NodeType == ExpressionType.Convert || node.NodeType == ExpressionType.ConvertChecked)
        {
            Visit(node.Operand);
            return node;
        }
        return base.VisitUnary(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression param)
        {
            var descriptor = _registry.GetDescriptor(param.Type);
            var col = descriptor.Columns.FirstOrDefault(c =>
                string.Equals(c.PropertyName, node.Member.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new UnmappedPropertyException(param.Type, node.Member.Name);
            if (_unaliased)
                _sql.Append(col.ColumnName);
            else
                _sql.Append($"{_aliases.GetAlias(param.Type)}.{col.ColumnName}");
        }
        else
        {
            // Captured variable — record source Func for plan cache, evaluate now for current call
            var captured = node; // capture for closure
            var paramName = _params.AddDynamic(() => EvaluateExpression(captured), node.Member.Name.ToLower());
            _sql.Append(paramName);
        }
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value == null)
        {
            _sql.Append("NULL");
            return node;
        }
        var paramName = _params.Add(node.Value);
        _sql.Append(paramName);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        switch (node.Method.Name)
        {
            case "Contains" when node.Object != null && node.Arguments.Count == 1:
                return EmitLike(node.Object, node.Arguments[0], "%{0}%");
            case "StartsWith" when node.Object != null && node.Arguments.Count >= 1:
                return EmitLike(node.Object, node.Arguments[0], "{0}%");
            case "EndsWith" when node.Object != null && node.Arguments.Count >= 1:
                return EmitLike(node.Object, node.Arguments[0], "%{0}");
            case "Contains" when node.Object == null && node.Arguments.Count == 2:
                // static Enumerable.Contains(collection, item)
                return EmitIn(node.Arguments[1], node.Arguments[0]);
            default:
                throw new UnsupportedExpressionException(node);
        }
    }

    private Expression EmitLike(Expression colExpr, Expression valueExpr, string pattern)
    {
        Visit(colExpr);
        _sql.Append(" LIKE ");
        var value = EvaluateExpression(valueExpr);
        var formatted = string.Format(pattern, value);
        _sql.Append(_params.Add(formatted));
        return colExpr;
    }

    private Expression EmitIn(Expression colExpr, Expression collectionExpr)
    {
        Visit(colExpr);
        _sql.Append(" IN (");
        var collection = EvaluateExpression(collectionExpr) as System.Collections.IEnumerable;
        if (collection != null)
        {
            bool first = true;
            foreach (var item in collection)
            {
                if (!first) _sql.Append(", ");
                _sql.Append(_params.Add(item));
                first = false;
            }
        }
        _sql.Append(')');
        return colExpr;
    }

    private static object? EvaluateExpression(Expression expr)
    {
        var lambda = Expression.Lambda(expr);
        return lambda.CompileFast().DynamicInvoke();
    }

    private static string MapOperator(ExpressionType type) => type switch
    {
        ExpressionType.Equal => " = ",
        ExpressionType.NotEqual => " <> ",
        ExpressionType.GreaterThan => " > ",
        ExpressionType.GreaterThanOrEqual => " >= ",
        ExpressionType.LessThan => " < ",
        ExpressionType.LessThanOrEqual => " <= ",
        ExpressionType.AndAlso => " AND ",
        ExpressionType.OrElse => " OR ",
        ExpressionType.Add => " + ",
        ExpressionType.Subtract => " - ",
        ExpressionType.Multiply => " * ",
        ExpressionType.Divide => " / ",
        _ => throw new UnsupportedExpressionException(Expression.Constant(type))
    };
}
