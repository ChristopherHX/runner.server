﻿using System;
using System.Collections.Generic;
using System.Linq;
using GitHub.DistributedTask.Expressions2;
using GitHub.DistributedTask.Expressions2.Sdk;

namespace GitHub.DistributedTask.ObjectTemplating.Tokens
{
    public static class TemplateTokenExtensions
    {
        internal static BooleanToken AssertBoolean(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is BooleanToken booleanToken)
            {
                return booleanToken;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(BooleanToken)}' was expected.");
        }

        internal static NullToken AssertNull(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is NullToken nullToken)
            {
                return nullToken;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(NullToken)}' was expected.");
        }

        internal static NumberToken AssertNumber(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is NumberToken numberToken)
            {
                return numberToken;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(NumberToken)}' was expected.");
        }

        internal static StringToken AssertString(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is StringToken stringToken)
            {
                return stringToken;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(StringToken)}' was expected.");
        }

        internal static MappingToken AssertMapping(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is MappingToken mapping)
            {
                return mapping;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(MappingToken)}' was expected.");
        }

        internal static void AssertNotEmpty(
            this MappingToken mapping,
            string objectDescription)
        {
            if (mapping.Count == 0)
            {
                throw new ArgumentException($"Unexpected empty mapping when reading '{objectDescription}'");
            }
        }

        internal static ScalarToken AssertScalar(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is ScalarToken scalar)
            {
                return scalar;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(ScalarToken)}' was expected.");
        }

        internal static SequenceToken AssertSequence(
            this TemplateToken value,
            string objectDescription)
        {
            if (value is SequenceToken sequence)
            {
                return sequence;
            }

            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(SequenceToken)}' was expected.");
        }

        public static SequenceToken AssertScalarOrSequence(this TemplateToken token, string objectDescription) {
            switch(token.Type) {
                case TokenType.Boolean:
                case TokenType.Number:
                case TokenType.String:
                    var seq = new SequenceToken(null, null, null);
                    seq.Add(token);
                    return seq;
                default:
                    return token.AssertSequence(objectDescription);
            }
        }

        public static string AssertLiteralString(this TemplateToken value, string objectDescription) {
            if(value is LiteralToken literalToken) {
                return literalToken.ToString();
            }
            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(LiteralToken)}' was expected.");
        }

        public static bool TryParseAzurePipelinesBoolean(string b, out bool val) {
            if(string.Equals(literalString, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "on", StringComparison.OrdinalIgnoreCase)) {
                val = true;
                return true;
            }
            if(string.Equals(literalString, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "f", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(literalString, "off", StringComparison.OrdinalIgnoreCase)) {
                val = false;
                return true;
            }
            val = false;
            return false;
        }

        public static bool AssertAzurePipelinesBoolean(this TemplateToken value, string objectDescription) {
            if(value is LiteralToken literalToken) {
                var literalString = literalToken.ToString();
                if(TryParseAzurePipelinesBoolean(literalString, out var ret)) {
                    return ret;
                }
            }
            throw new ArgumentException($"Unexpected type '{value?.GetType().Name}' encountered while reading '{objectDescription}'. The type '{nameof(LiteralToken)}' with value true | y | yes | on | false | n | no | off was expected.");
        }

        internal static void AssertUnexpectedValue(
            this LiteralToken literal,
            string objectDescription)
        {
            throw new ArgumentException($"Error while reading '{objectDescription}'. Unexpected value '{literal.ToString()}'");
        }

        /// <summary>
        /// Traverses the token and checks whether all required expression values
        /// and functions are provided.
        /// </summary>
        public static bool CheckHasRequiredContext(
            this TemplateToken token,
            IReadOnlyObject expressionValues,
            IList<IFunctionInfo> expressionFunctions,
            ExpressionFlags flags = ExpressionFlags.None)
        {
            var expressionTokens = token.Traverse()
                .OfType<BasicExpressionToken>()
                .ToArray();
            var parser = new ExpressionParser() { Flags = flags };
            foreach (var expressionToken in expressionTokens)
            {
                var tree = parser.ValidateSyntax(expressionToken.Expression, null);
                foreach (var node in tree.Traverse())
                {
                    if (node is NamedValue namedValue)
                    {
                        if (expressionValues?.Keys.Any(x => string.Equals(x, namedValue.Name, StringComparison.OrdinalIgnoreCase)) != true)
                        {
                            return false;
                        }
                    }
                    else if (node is Function function &&
                        !((flags & ExpressionFlags.ExtendedFunctions) == ExpressionFlags.DTExpressionsV1 ? ExpressionConstants.AzureWellKnownFunctions : ExpressionConstants.WellKnownFunctions).ContainsKey(function.Name) &&
                        expressionFunctions?.Any(x => string.Equals(x.Name, function.Name, StringComparison.OrdinalIgnoreCase)) != true)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns all tokens (depth first)
        /// </summary>
        public static IEnumerable<TemplateToken> Traverse(this TemplateToken token)
        {
            return Traverse(token, omitKeys: false);
        }

        /// <summary>
        /// Returns all tokens (depth first)
        /// </summary>
        public static IEnumerable<TemplateToken> Traverse(
            this TemplateToken token,
            bool omitKeys)
        {
            if (token != null)
            {
                yield return token;

                if (token is SequenceToken || token is MappingToken)
                {
                    var state = new TraversalState(null, token);
                    while (state != null)
                    {
                        if (state.MoveNext(omitKeys))
                        {
                            token = state.Current;
                            yield return token;

                            if (token is SequenceToken || token is MappingToken)
                            {
                                state = new TraversalState(state, token);
                            }
                        }
                        else
                        {
                            state = state.Parent;
                        }
                    }
                }
            }
        }

        private sealed class TraversalState
        {
            public TraversalState(
                TraversalState parent,
                TemplateToken token)
            {
                Parent = parent;
                m_token = token;
            }

            public bool MoveNext(bool omitKeys)
            {
                switch (m_token.Type)
                {
                    case TokenType.Sequence:
                        var sequence = m_token as SequenceToken;
                        if (++m_index < sequence.Count)
                        {
                            Current = sequence[m_index];
                            return true;
                        }
                        else
                        {
                            Current = null;
                            return false;
                        }

                    case TokenType.Mapping:
                        var mapping = m_token as MappingToken;

                        // Return the value
                        if (m_isKey)
                        {
                            m_isKey = false;
                            Current = mapping[m_index].Value;
                            return true;
                        }

                        if (++m_index < mapping.Count)
                        {
                            // Skip the key, return the value
                            if (omitKeys)
                            {
                                m_isKey = false;
                                Current = mapping[m_index].Value;
                                return true;
                            }

                            // Return the key
                            m_isKey = true;
                            Current = mapping[m_index].Key;
                            return true;
                        }

                        Current = null;
                        return false;

                    default:
                        throw new NotSupportedException($"Unexpected token type '{m_token.Type}'");
                }
            }

            private TemplateToken m_token;
            private int m_index = -1;
            private bool m_isKey;
            public TemplateToken Current;
            public TraversalState Parent;
        }
    }
}
