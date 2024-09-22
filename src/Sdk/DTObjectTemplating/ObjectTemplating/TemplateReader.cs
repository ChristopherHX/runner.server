﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GitHub.DistributedTask.Expressions2.Sdk;
using GitHub.DistributedTask.ObjectTemplating.Tokens;
using GitHub.DistributedTask.ObjectTemplating.Schema;
using GitHub.DistributedTask.Expressions2.Tokens;
using System.IO;

namespace GitHub.DistributedTask.ObjectTemplating
{
    /// <summary>
    /// Converts a source object format into a TemplateToken
    /// </summary>
    internal sealed class TemplateReader
    {
        private TemplateReader(
            TemplateContext context,
            TemplateSchema schema,
            IObjectReader objectReader,
            Int32? fileId)
        {
            m_context = context;
            m_schema = schema;
            m_memory = context.Memory;
            m_objectReader = objectReader;
            m_fileId = fileId;
        }

        internal static TemplateToken Read(
            TemplateContext context,
            String type,
            IObjectReader objectReader,
            Int32? fileId,
            out Int32 bytes)
        {
            return Read(context, type, objectReader, fileId, context.Schema, out bytes);
        }

        internal static TemplateToken Read(
            TemplateContext context,
            String type,
            IObjectReader objectReader,
            Int32? fileId,
            TemplateSchema schema,
            out Int32 bytes)
        {
            TemplateToken result = null;

            var reader = new TemplateReader(context, schema, objectReader, fileId);
            var originalBytes = context.Memory.CurrentBytes;
            try
            {
                objectReader.ValidateStart();
                var definition = new DefinitionInfo(schema, type);
                result = reader.ReadValue(definition);
                objectReader.ValidateEnd();
            }
            catch (Exception ex)
            {
                context.Error(fileId, null, null, ex);
            }
            finally
            {
                bytes = context.Memory.CurrentBytes - originalBytes;
            }

            return result;
        }

        private bool Match(TemplateToken token) {
            if(token.PreWhiteSpace != null) {
                return m_context.Row > token.PreWhiteSpace.Line || m_context.Row == token.PreWhiteSpace.Line && m_context.Column >= token.PreWhiteSpace.Character;
            }
            return m_context.Row >= token.Line && m_context.Column >= token.Column;
        }

        private bool MatchPost(TemplateToken token) {
            return m_context.Row < token.PostWhiteSpace.Line || m_context.Row == token.PostWhiteSpace.Line && m_context.Column <= token.PostWhiteSpace.Character;
        }

        private TemplateToken ReadValue(DefinitionInfo definition)
        {
            m_memory.IncrementEvents();

            // Scalar
            if (m_objectReader.AllowLiteral(out LiteralToken literal))
            {
                var scalar = ParseScalar(literal, definition);
                Validate(ref scalar, definition);
                m_memory.AddBytes(scalar);
                return scalar;
            }

            // Sequence
            if (m_objectReader.AllowSequenceStart(out SequenceToken sequence))
            {
                if(m_context.AutoCompleteMatches != null && Match(sequence)) {
                    m_context.AutoCompleteMatches.RemoveAll(m => m.Depth >= m_memory.Depth);
                    m_context.AutoCompleteMatches.Add(new AutoCompleteEntry {
                        Depth = m_memory.Depth,
                        Token = sequence,
                        AllowedContext = definition.AllowedContext,
                        Definitions = new [] { definition.Definition }
                    });
                }
                m_memory.IncrementDepth();
                m_memory.AddBytes(sequence);

                var sequenceDefinition = definition.Get<SequenceDefinition>().FirstOrDefault();

                // Legal
                if (sequenceDefinition != null)
                {
                    var itemDefinition = new DefinitionInfo(definition, sequenceDefinition.ItemType);

                    // Add each item
                    while (!m_objectReader.AllowSequenceEnd(sequence))
                    {
                        var item = ReadValue(itemDefinition);
                        sequence.Add(item);
                    }
                }
                // Illegal
                else
                {
                    // Error
                    m_context.Error(sequence, TemplateStrings.UnexpectedSequenceStart());

                    // Skip each item
                    while (!m_objectReader.AllowSequenceEnd())
                    {
                        SkipValue();
                    }
                }

                m_memory.DecrementDepth();
                return sequence;
            }

            // Mapping
            if (m_objectReader.AllowMappingStart(out MappingToken mapping))
            {
                if(m_context.AutoCompleteMatches != null && Match(mapping)) {
                    m_context.AutoCompleteMatches.RemoveAll(m => m.Depth >= m_memory.Depth);
                    m_context.AutoCompleteMatches.Add(new AutoCompleteEntry {
                        Depth = m_memory.Depth,
                        Token = mapping,
                        AllowedContext = definition.AllowedContext,
                        Definitions = new [] { definition.Definition }
                    });
                }
                m_memory.IncrementDepth();
                m_memory.AddBytes(mapping);

                var mappingDefinitions = definition.Get<MappingDefinition>().ToList();

                // Legal
                if (mappingDefinitions.Count > 0)
                {
                    if (mappingDefinitions.Count > 1 ||
                        m_schema.HasProperties(mappingDefinitions[0]) ||
                        String.IsNullOrEmpty(mappingDefinitions[0].LooseKeyType))
                    {
                        HandleMappingWithWellKnownProperties(definition, mappingDefinitions, mapping);
                    }
                    else
                    {
                        var keyDefinition = new DefinitionInfo(definition, mappingDefinitions[0].LooseKeyType);
                        var valueDefinition = new DefinitionInfo(definition, mappingDefinitions[0].LooseValueType);
                        HandleMappingWithAllLooseProperties(definition, keyDefinition, valueDefinition, mapping);
                    }
                }
                // Illegal
                else
                {
                    m_context.Error(mapping, TemplateStrings.UnexpectedMappingStart());

                    while (!m_objectReader.AllowMappingEnd())
                    {
                        SkipValue();
                        SkipValue();
                    }
                }

                m_memory.DecrementDepth();
                return mapping;
            }

            throw new InvalidOperationException(TemplateStrings.ExpectedScalarSequenceOrMapping());
        }

        private void HandleMappingWithWellKnownProperties(
            DefinitionInfo definition,
            List<MappingDefinition> mappingDefinitions,
            MappingToken mapping)
        {
            // Check if loose properties are allowed
            String looseKeyType = null;
            String looseValueType = null;
            DefinitionInfo? looseKeyDefinition = null;
            DefinitionInfo? looseValueDefinition = null;
            if (!String.IsNullOrEmpty(mappingDefinitions[0].LooseKeyType))
            {
                looseKeyType = mappingDefinitions[0].LooseKeyType;
                looseValueType = mappingDefinitions[0].LooseValueType;
            }

            var keys = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var hasExpressionKey = false;
            int i = 0;
            while (m_objectReader.AllowLiteral(out LiteralToken rawLiteral))
            {
                var firstKey = i++ == 0;
                var nextKeyScalar = ParseScalar(rawLiteral, definition);
                // Expression
                if (nextKeyScalar is ExpressionToken)
                {
                    hasExpressionKey = true;
                    // Legal
                    if (definition.AllowedContext.Length > 0)
                    {
                        m_memory.AddBytes(nextKeyScalar);
                        TemplateToken nextValue;
                        var anyDefinition = new DefinitionInfo(definition, TemplateConstants.Any);
                        if(nextKeyScalar is EachExpressionToken eachexp) {
                            var def = new DefinitionInfo(definition, "any");
                            def.AllowedContext = definition.AllowedContext.Append(eachexp.Variable).ToArray();
                            nextValue = ReadValue(def);
                        } else if(nextKeyScalar is ConditionalExpressionToken || nextKeyScalar is InsertExpressionToken && (m_context.Flags & Expressions2.ExpressionFlags.AllowAnyForInsert) != Expressions2.ExpressionFlags.None) {
                            var def = new DefinitionInfo(definition, "any");
                            nextValue = ReadValue(def);
                        } else {
                            nextValue = ReadValue(anyDefinition);
                        }
                        mapping.Add(nextKeyScalar, nextValue);
                    }
                    // Illegal
                    else
                    {
                        m_context.Error(nextKeyScalar, TemplateStrings.ExpressionNotAllowed());
                        SkipValue();
                    }

                    continue;
                }

                // Not a string, convert
                if (!(nextKeyScalar is StringToken nextKey))
                {
                    nextKey = new StringToken(nextKeyScalar.FileId, nextKeyScalar.Line, nextKeyScalar.Column, nextKeyScalar.ToString());
                }

                // Duplicate
                if (!keys.Add(nextKey.Value))
                {
                    m_context.Error(nextKey, TemplateStrings.ValueAlreadyDefined(nextKey.Value));
                    SkipValue();
                    continue;
                }

                // Well known
                if (m_schema.TryMatchKey(mappingDefinitions, nextKey.Value, out String nextValueType, firstKey))
                {
                    m_memory.AddBytes(nextKey);
                    var nextValueDefinition = new DefinitionInfo(definition, nextValueType);
                    var nextValue = ReadValue(nextValueDefinition);
                    mapping.Add(nextKey, nextValue);
                    continue;
                }

                // Loose
                if (looseKeyType != null)
                {
                    if (looseKeyDefinition == null)
                    {
                        looseKeyDefinition = new DefinitionInfo(definition, looseKeyType);
                        looseValueDefinition = new DefinitionInfo(definition, looseValueType);
                    }

                    Validate(nextKey, looseKeyDefinition.Value);
                    m_memory.AddBytes(nextKey);
                    var nextValue = ReadValue(looseValueDefinition.Value);
                    mapping.Add(nextKey, nextValue);
                    continue;
                }

                // Error
                m_context.Error(nextKey, TemplateStrings.UnexpectedValue(nextKey.Value));
                SkipValue();
            }

            if(m_context.AutoCompleteMatches != null) {
                var aentry = m_context.AutoCompleteMatches.Where(a => a.Token == mapping).FirstOrDefault();
                if(aentry != null) {
                    aentry.Definitions = mappingDefinitions.Cast<Definition>().ToArray();
                }
            }

            // Only one
            if (mappingDefinitions.Count > 1 && !hasExpressionKey)
            {
                var hitCount = new Dictionary<String, Int32>();
                foreach (MappingDefinition mapdef in mappingDefinitions)
                {
                    foreach (var kv in mapdef.Properties)
                    {
                        var key = kv.Key;
                        if (!hitCount.TryGetValue(key, out Int32 value))
                        {
                            hitCount.Add(key, 1);
                        }
                        else
                        {
                            hitCount[key] = value + 1;
                        }
                    }
                }

                List<String> nonDuplicates = new List<String>();
                foreach (String key in hitCount.Keys)
                {
                    if (hitCount[key] == 1)
                    {
                        nonDuplicates.Add(key);
                    }
                }
                nonDuplicates.Sort();

                String listToDeDuplicate = String.Join(", ", nonDuplicates);
                m_context.Error(mapping, TemplateStrings.UnableToDetermineOneOf(listToDeDuplicate));
            }
            else if (mappingDefinitions.Count == 1 && !hasExpressionKey)
            {
                foreach (var property in mappingDefinitions[0].Properties)
                {
                    if (property.Value.Required)
                    {
                        if (!keys.Contains(property.Key))
                        {
                            m_context.Error(mapping, $"Required property is missing: {property.Key}");
                        }
                    }
                }
            }
            ExpectMappingEnd(mapping);
        }

        private void HandleMappingWithAllLooseProperties(
            DefinitionInfo mappingDefinition,
            DefinitionInfo keyDefinition,
            DefinitionInfo valueDefinition,
            MappingToken mapping)
        {
            TemplateToken nextValue;
            var keys = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

            while (m_objectReader.AllowLiteral(out LiteralToken rawLiteral))
            {
                var nextKeyScalar = ParseScalar(rawLiteral, mappingDefinition);

                // Expression
                if (nextKeyScalar is ExpressionToken)
                {
                    // Legal
                    if (mappingDefinition.AllowedContext.Length > 0)
                    {
                        m_memory.AddBytes(nextKeyScalar);
                        if(nextKeyScalar is EachExpressionToken eachexp) {
                            var def = new DefinitionInfo(mappingDefinition, "any");
                            def.AllowedContext = valueDefinition.AllowedContext.Append(eachexp.Variable).ToArray();
                            nextValue = ReadValue(def);
                        } else if(nextKeyScalar is ConditionalExpressionToken || nextKeyScalar is InsertExpressionToken && (m_context.Flags & Expressions2.ExpressionFlags.AllowAnyForInsert) != Expressions2.ExpressionFlags.None) {
                            var def = new DefinitionInfo(mappingDefinition, "any");
                            nextValue = ReadValue(def);
                        } else {
                            nextValue = ReadValue(valueDefinition);
                        }
                        mapping.Add(nextKeyScalar, nextValue);
                    }
                    // Illegal
                    else
                    {
                        m_context.Error(nextKeyScalar, TemplateStrings.ExpressionNotAllowed());
                        SkipValue();
                    }

                    continue;
                }

                // Not a string, convert
                if (!(nextKeyScalar is StringToken nextKey))
                {
                    nextKey = new StringToken(nextKeyScalar.FileId, nextKeyScalar.Line, nextKeyScalar.Column, nextKeyScalar.ToString());
                }

                // Duplicate
                if (!keys.Add(nextKey.Value))
                {
                    m_context.Error(nextKey, TemplateStrings.ValueAlreadyDefined(nextKey.Value));
                    SkipValue();
                    continue;
                }

                // Validate
                Validate(nextKey, keyDefinition);
                m_memory.AddBytes(nextKey);

                // Add the pair
                nextValue = ReadValue(valueDefinition);
                mapping.Add(nextKey, nextValue);
            }

            ExpectMappingEnd(mapping);
        }

        private void ExpectMappingEnd(MappingToken token)
        {
            if (!m_objectReader.AllowMappingEnd(token))
            {
                throw new Exception("Expected mapping end"); // Should never happen
            }
            if(m_context.AutoCompleteMatches != null && token.PostWhiteSpace != null && !MatchPost(token)) {
                var completion = m_context.AutoCompleteMatches.FirstOrDefault(m => m.Token == token);
                if(completion != null) {
                    m_context.AutoCompleteMatches.RemoveAll(m => m.Depth >= completion.Depth);
                }
            }
        }

        private void SkipValue(Boolean error = false)
        {
            m_memory.IncrementEvents();

            // Scalar
            if (m_objectReader.AllowLiteral(out LiteralToken literal))
            {
                if (error)
                {
                    m_context.Error(literal, TemplateStrings.UnexpectedValue(literal));
                }

                return;
            }

            // Sequence
            if (m_objectReader.AllowSequenceStart(out SequenceToken sequence))
            {
                m_memory.IncrementDepth();

                if (error)
                {
                    m_context.Error(sequence, TemplateStrings.UnexpectedSequenceStart());
                }

                while (!m_objectReader.AllowSequenceEnd())
                {
                    SkipValue();
                }

                m_memory.DecrementDepth();
                return;
            }

            // Mapping
            if (m_objectReader.AllowMappingStart(out MappingToken mapping))
            {
                m_memory.IncrementDepth();

                if (error)
                {
                    m_context.Error(mapping, TemplateStrings.UnexpectedMappingStart());
                }

                while (!m_objectReader.AllowMappingEnd())
                {
                    SkipValue();
                    SkipValue();
                }

                m_memory.DecrementDepth();
                return;
            }

            // Unexpected
            throw new InvalidOperationException(TemplateStrings.ExpectedScalarSequenceOrMapping());
        }

        private void Validate(
            StringToken stringToken,
            DefinitionInfo definition)
        {
            var scalar = stringToken as ScalarToken;
            Validate(ref scalar, definition);
        }

        private void Validate(
            ref ScalarToken scalar,
            DefinitionInfo definition)
        {
            switch (scalar.Type)
            {
                case TokenType.Null:
                case TokenType.Boolean:
                case TokenType.Number:
                case TokenType.String:
                    var literal = scalar as LiteralToken;

                    // Legal
                    if (definition.Get<ScalarDefinition>().Any(x => x.IsMatch(literal)))
                    {
                        return;
                    }

                    // Not a string, convert
                    if (literal.Type != TokenType.String)
                    {
                        literal = new StringToken(literal.FileId, literal.Line, literal.Column, literal.ToString());

                        // Legal
                        if (definition.Get<StringDefinition>().Any(x => x.IsMatch(literal)))
                        {
                            scalar = literal;
                            return;
                        }
                    }

                    // Illegal
                    m_context.Error(literal, TemplateStrings.UnexpectedValue(literal));
                    break;

                case TokenType.BasicExpression:

                    // Illegal
                    if (definition.AllowedContext.Length == 0)
                    {
                        m_context.Error(scalar, TemplateStrings.ExpressionNotAllowed());
                    }

                    break;

                default:
                    m_context.Error(scalar, TemplateStrings.UnexpectedValue(scalar));
                    break;
            }
        }

        private ScalarToken ParseScalar(
            LiteralToken token,
            DefinitionInfo definitionInfo)
        {
            AutoCompleteEntry completion = null;//m_context.Row >= token.Line 
            if(m_context.AutoCompleteMatches != null && Match(token) && (token.PostWhiteSpace == null || MatchPost(token) /*(m_context.Row < token.PostWhiteSpace.Line && !(token.PostWhiteSpace.Line == m_context.Row && token.PostWhiteSpace.Character > m_context.Column))*/)) {
                m_context.AutoCompleteMatches.RemoveAll(m => m.Depth >= m_memory.Depth);
                m_context.AutoCompleteMatches.Add(completion = new AutoCompleteEntry {
                    Depth = m_memory.Depth,
                    Token = token,
                    AllowedContext = definitionInfo.AllowedContext,
                    Definitions = new [] { definitionInfo.Definition }
                });
            }
            var allowedContext = definitionInfo.AllowedContext;
            var isExpression = definitionInfo.Definition is StringDefinition sdef && sdef.IsExpression;
            var actionsIfExpression = definitionInfo.Definition.ActionsIfExpression || isExpression;
            // Not a string
            if (token.Type != TokenType.String)
            {
                return token;
            }

            // Check if the value is definitely a literal
            var raw = token.ToString();
            Int32 startExpression;
            if (String.IsNullOrEmpty(raw) ||
                (startExpression = raw.IndexOf(TemplateConstants.OpenExpression)) < 0) // Doesn't contain ${{
            {
                if(!String.IsNullOrEmpty(raw) && isExpression) {
                    if(completion != null && completion.Index < 0) {
                        completion.Index = -1;
                    }
                    // Check if value should still be evaluated as an expression
                    var expression = ParseExpression(completion, token.Line, token.Column, raw, allowedContext, out Exception ex);
                    // Check for error
                    if (ex != null) {
                        m_context.Error(token, ex);
                    } else {
                        return expression;
                    }
                }
                return token;
            }

            // Break the value into segments of LiteralToken and ExpressionToken
            var segments = new List<ScalarToken>();
            var i = 0;
            while (i < raw.Length)
            {
                // An expression starts here:
                if (i == startExpression)
                {
                    // Find the end of the expression - i.e. }}
                    startExpression = i;
                    var endExpression = -1;
                    var inString = false;
                    for (i += TemplateConstants.OpenExpression.Length; i < raw.Length; i++)
                    {
                        if (raw[i] == '\'')
                        {
                            inString = !inString; // Note, this handles escaped single quotes gracefully. Ex. 'foo''bar'
                        }
                        else if (!inString && raw[i] == '}' && raw[i - 1] == '}')
                        {
                            endExpression = i;
                            i++;
                            break;
                        }
                    }

                    // if(m_context.AutoCompleteMatches != null) {

                    //     var idx = GetIdxOfExpression(token, m_context.Row.Value, m_context.Column.Value);
                    //     var startIndex = startExpression + TemplateConstants.OpenExpression.Length;
                    //     if(idx != -1 && idx >= startIndex && (endExpression == -1 || idx <= endExpression + 1 - TemplateConstants.CloseExpression.Length)) {
                    //         LexicalAnalyzer lexicalAnalyzer = new LexicalAnalyzer(raw.Substring(
                    //             startIndex,
                    //             endExpression == -1 ? raw.Length - startIndex : endExpression + 1 - startIndex - TemplateConstants.CloseExpression.Length), m_context.Flags);
                    //         Token tkn = null;
                    //         List<Token> tkns = new List<Token>();
                    //         while(lexicalAnalyzer.TryGetNextToken(ref tkn)) {
                    //             tkns.Add(tkn);
                    //             if(tkn.Index + startExpression + TemplateConstants.OpenExpression.Length > idx) {
                    //                 break;
                    //             }
                    //         }
                    //         m_context.AutoCompleteMatches.Add(new AutoCompleteEntry() {
                    //             Depth = m_memory.Depth,
                    //             AllowedContext = allowedContext,
                    //             Definitions = new Definition[] { definitionInfo.Definition },
                    //             Token = token,
                    //             Tokens = tkns,
                    //             Index = idx - (startExpression + TemplateConstants.OpenExpression.Length)
                    //         });
                    //     }
                    // }

                    // Check if not closed
                    if (endExpression < startExpression)
                    {
                        m_context.Error(token, TemplateStrings.ExpressionNotClosed());
                        if(completion == null) {
                            return token;
                        }
                        endExpression = raw.Length + TemplateConstants.CloseExpression.Length - 1;
                    }
                    if(completion != null && completion.Index < 0) {
                        completion.Index = - (startExpression + TemplateConstants.OpenExpression.Length + 1);
                    }

                    // Parse the expression
                    var rawExpression = raw.Substring(
                        startExpression + TemplateConstants.OpenExpression.Length,
                        endExpression - startExpression + 1 - TemplateConstants.OpenExpression.Length - TemplateConstants.CloseExpression.Length);
                    var expression = ParseExpression(completion, token.Line, token.Column, rawExpression, allowedContext, out Exception ex);

                    // Check for error
                    if (ex != null)
                    {
                        m_context.Error(token, ex);
                        return token;
                    }

                    // Check if a directive was used when not allowed
                    if (!String.IsNullOrEmpty(expression.Directive) &&
                        ((startExpression != 0) || (i < raw.Length)))
                    {
                        m_context.Error(token, TemplateStrings.DirectiveNotAllowedInline(expression.Directive));
                        return token;
                    }

                    // Add the segment
                    segments.Add(expression);

                    // Look for the next expression
                    startExpression = raw.IndexOf(TemplateConstants.OpenExpression, i);
                }
                // The next expression is further ahead:
                else if (i < startExpression)
                {
                    // Append the segment
                    AddString(segments, token.Line, token.Column, raw.Substring(i, startExpression - i));

                    // Adjust the position
                    i = startExpression;
                }
                // No remaining expressions:
                else
                {
                    AddString(segments, token.Line, token.Column, raw.Substring(i));
                    break;
                }
            }

            // Check if can convert to a literal
            // For example, the escaped expression: ${{ '{{ this is a literal }}' }}
            if (segments.Count == 1 &&
                segments[0] is BasicExpressionToken basicExpression &&
                IsExpressionString(basicExpression.Expression, out String str))
            {
                return new StringToken(m_fileId, token.Line, token.Column, str);
            }

            // Check if only ony segment
            if (segments.Count == 1)
            {
                return segments[0];
            }

            if (actionsIfExpression && (m_context.Flags & Expressions2.ExpressionFlags.FailInvalidActionsIfExpression) != Expressions2.ExpressionFlags.None)
            {
                m_context.Error(token, $"If condition has been converted to format expression and won't evaluate correctly: {raw}");
            }
            if (actionsIfExpression && (m_context.Flags & Expressions2.ExpressionFlags.FixInvalidActionsIfExpression) != Expressions2.ExpressionFlags.None)
            {
                var fixedExpression = new StringBuilder();
                foreach (var segment in segments)
                {
                    if (segment is StringToken literal)
                    {
                        fixedExpression.Append(literal.Value);
                    }
                    else
                    {
                        fixedExpression.Append((segment as BasicExpressionToken).Expression);
                    }
                }
                return new BasicExpressionToken(m_fileId, token.Line, token.Column, fixedExpression.ToString());
            }

            // Build the new expression, using the format function
            var format = new StringBuilder();
            var args = new StringBuilder();
            var argIndex = 0;
            foreach (var segment in segments)
            {
                if (segment is StringToken literal)
                {
                    var text = ExpressionUtility.StringEscape(literal.Value) // Escape quotes
                        .Replace("{", "{{") // Escape braces
                        .Replace("}", "}}");
                    format.Append(text);
                }
                else
                {
                    format.Append("{" + argIndex.ToString(CultureInfo.InvariantCulture) + "}"); // Append formatter
                    argIndex++;

                    var expression = segment as BasicExpressionToken;
                    args.Append(", ");
                    args.Append(expression.Expression);
                }
            }

            return new BasicExpressionToken(m_fileId, token.Line, token.Column, $"format('{format}'{args})");
        }

        private bool AutoCompleteExpression(AutoCompleteEntry completion, int offset, string value, int poffset = 0) {
            if(completion != null && m_context.AutoCompleteMatches != null) {
                var idx = GetIdxOfExpression(completion.Token as LiteralToken, m_context.Row.Value, m_context.Column.Value);
                var startIndex = -1 - completion.Index + offset;
                if(idx != -1 && idx >= startIndex && (idx <= startIndex + value.Length + poffset)) {
                    LexicalAnalyzer lexicalAnalyzer = new LexicalAnalyzer(value, m_context.Flags);
                    Token tkn = null;
                    List<Token> tkns = new List<Token>();
                    while(lexicalAnalyzer.TryGetNextToken(ref tkn)) {
                        tkns.Add(tkn);
                        if(tkn.Index + startIndex > idx) {
                            break;
                        }
                    }
                    completion.Tokens = tkns;
                    completion.Index = idx - startIndex;
                }
            }
            return true;
        }

        private static int GetIdxOfExpression(LiteralToken lit, int row, int column)
        {
          var lc = column - lit.Column;
          var lr = row - lit.Line;
          var rand = new Random();
          string C = "CX";
          while(lit.RawData.Contains(C)) {
            C = rand.Next(255).ToString("X2");
          }
          var xraw = lit.RawData;
          var idx = 0;
          for(int i = 0; i < lr; i++) {
            var n = xraw.IndexOf('\n', idx);
            if(n == -1) {
              return -1;
            }
            idx = n + 1;
          }
          idx += idx == 0 ? lc ?? 0 : column - 1;
          if(idx > xraw.Length) {
            return -1;
          }
          xraw = xraw.Insert(idx, C);

          var scanner = new YamlDotNet.Core.Scanner(new StringReader(xraw), true);
          try {
            while(scanner.MoveNext()) {
              if(scanner.Current is YamlDotNet.Core.Tokens.Scalar s) {
                var x = s.Value;
                return x.IndexOf(C);
              }
            }
          } catch {

          }
          return -1;
        }

        private ExpressionToken ParseExpression(
            AutoCompleteEntry completion,
            Int32? line,
            Int32? column,
            String value,
            String[] allowedContext,
            out Exception ex)
        {
            // TODO !!!!! If the expressions parameter is missing in directives provide auto completion
            // It's buggy
            // Empty expressions like ${{  }} are not auto completed?
            var trimmed = value.Trim();

            // Check if the value is empty
            if (String.IsNullOrEmpty(trimmed))
            {
                AutoCompleteExpression(completion, 0, value);
                ex = new ArgumentException(TemplateStrings.ExpectedExpression());
                return null;
            }
            var trimmedNo = value.IndexOf(trimmed);

            bool extendedDirectives = (m_context.Flags & Expressions2.ExpressionFlags.ExtendedDirectives) != Expressions2.ExpressionFlags.None;
            // Try to find a matching directive
            List<(int, String)> parameters;
            if (MatchesDirective(trimmed, TemplateConstants.InsertDirective, 0, out parameters, out ex))
            {
                return new InsertExpressionToken(m_fileId, line, column);
            }
            else if (ex != null)
            {
                return null;
            }
            else if (extendedDirectives && MatchesDirective(trimmed, "if", 1, out parameters, out ex) && AutoCompleteExpression(completion, trimmedNo + parameters[0].Item1, value) && ExpressionToken.IsValidExpression(parameters[0].Item2, allowedContext, out ex, m_context.Flags) || parameters?.Count == 1 && !AutoCompleteExpression(completion, trimmedNo + parameters[0].Item1, ""))
            {
                return new IfExpressionToken(m_fileId, line, column, parameters[0].Item2);
            }
            else if (ex != null)
            {
                return null;
            }
            else if (extendedDirectives && MatchesDirective(trimmed, "elseif", 1, out parameters, out ex) && AutoCompleteExpression(completion, trimmedNo + parameters[0].Item1, value) && ExpressionToken.IsValidExpression(parameters[0].Item2, allowedContext, out ex, m_context.Flags) || parameters?.Count == 1 && !AutoCompleteExpression(completion, trimmedNo + parameters[0].Item1, ""))
            {
                return new ElseIfExpressionToken(m_fileId, line, column, parameters[0].Item2);
            }
            else if (ex != null)
            {
                return null;
            }
            else if (extendedDirectives && MatchesDirective(trimmed, "else", 0, out parameters, out ex))
            {
                return new ElseExpressionToken(m_fileId, line, column);
            }
            else if (ex != null)
            {
                return null;
            }
            else if (extendedDirectives && MatchesDirective(trimmed, "each", 3, out parameters, out ex) && parameters[1].Item2 == "in" && AutoCompleteExpression(completion, trimmedNo + parameters[2].Item1, value) && ExpressionToken.IsValidExpression(parameters[2].Item2, allowedContext, out ex, m_context.Flags) || parameters?.Count == 3 && !AutoCompleteExpression(completion, trimmedNo + parameters[2].Item1, ""))
            {
                return new EachExpressionToken(m_fileId, line, column, parameters[0].Item2, parameters[2].Item2);
            }
            else if (ex != null)
            {
                return null;
            }

            AutoCompleteExpression(completion, 0, value);

            // Check if the value is an expression
            if (!ExpressionToken.IsValidExpression(trimmed, allowedContext, out ex, m_context.Flags))
            {
                return null;
            }

            // Return the expression
            return new BasicExpressionToken(m_fileId, line, column, trimmed);
        }

        private void AddString(
            List<ScalarToken> segments,
            Int32? line,
            Int32? column,
            String value)
        {
            // If the last segment was a LiteralToken, then append to the last segment
            if (segments.Count > 0 && segments[segments.Count - 1] is StringToken lastSegment)
            {
                segments[segments.Count - 1] = new StringToken(m_fileId, line, column, lastSegment.Value + value);
            }
            // Otherwise add a new LiteralToken
            else
            {
                segments.Add(new StringToken(m_fileId, line, column, value));
            }
        }

        private static Boolean MatchesDirective(
            String trimmed,
            String directive,
            Int32 expectedParameters,
            out List<(int, String)> parameters,
            out Exception ex)
        {
            if (trimmed.StartsWith(directive, StringComparison.Ordinal) &&
                (trimmed.Length == directive.Length || Char.IsWhiteSpace(trimmed[directive.Length])))
            {
                parameters = new List<(int, String)>();
                var startIndex = directive.Length;
                var inString = false;
                var parens = 0;
                for (var i = startIndex; i < trimmed.Length; i++)
                {
                    var c = trimmed[i];
                    if (Char.IsWhiteSpace(c) && !inString && parens == 0)
                    {
                        if (startIndex < i)
                        {
                            parameters.Add((startIndex, trimmed.Substring(startIndex, i - startIndex)));
                        }

                        startIndex = i + 1;
                    }
                    else if (c == '\'')
                    {
                        inString = !inString;
                    }
                    else if (c == '(' && !inString)
                    {
                        parens++;
                    }
                    else if (c == ')' && !inString)
                    {
                        parens--;
                    }
                }

                if (startIndex < trimmed.Length)
                {
                    parameters.Add((startIndex, trimmed.Substring(startIndex)));
                }

                if (expectedParameters != parameters.Count)
                {
                    ex = new ArgumentException(TemplateStrings.ExpectedNParametersFollowingDirective(expectedParameters, directive, parameters.Count));
                    if(expectedParameters == parameters.Count + 1) {
                        parameters.Add((parameters.Last().Item1 + parameters.Last().Item2.Length + 1, ""));
                    } else {
                        parameters = null;
                    }
                    return false;
                }

                ex = null;
                return true;
            }

            ex = null;
            parameters = null;
            return false;
        }

        private static Boolean IsExpressionString(
            String trimmed,
            out String str)
        {
            var builder = new StringBuilder();

            var inString = false;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (c == '\'')
                {
                    inString = !inString;

                    if (inString && i != 0)
                    {
                        builder.Append(c);
                    }
                }
                else if (!inString)
                {
                    str = default;
                    return false;
                }
                else
                {
                    builder.Append(c);
                }
            }

            str = builder.ToString();
            return true;
        }

        private struct DefinitionInfo
        {
            public DefinitionInfo(
                TemplateSchema schema,
                String name)
            {
                m_schema = schema;

                // Lookup the definition
                Definition = m_schema.GetDefinition(name);

                // Record allowed context
                AllowedContext = Definition.ReaderContext;
            }

            public DefinitionInfo(
                DefinitionInfo parent,
                String name)
            {
                m_schema = parent.m_schema;

                // Lookup the definition
                Definition = m_schema.GetDefinition(name);

                // Record allowed context
                if (Definition.ReaderContext.Length > 0)
                {
                    AllowedContext = new HashSet<String>(parent.AllowedContext.Concat(Definition.ReaderContext), StringComparer.OrdinalIgnoreCase).ToArray();
                }
                else
                {
                    AllowedContext = parent.AllowedContext;
                }
            }

            public IEnumerable<T> Get<T>()
                where T : Definition
            {
                return m_schema.Get<T>(Definition);
            }

            private TemplateSchema m_schema;
            public Definition Definition;
            public String[] AllowedContext;
        }

        private readonly TemplateContext m_context;
        private readonly Int32? m_fileId;
        private readonly TemplateMemory m_memory;
        private readonly IObjectReader m_objectReader;
        private readonly TemplateSchema m_schema;
    }
}
