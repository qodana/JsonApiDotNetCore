using System.Collections.Immutable;
using JetBrains.Annotations;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources.Annotations;

namespace JsonApiDotNetCore.Queries.Internal.Parsing;

[PublicAPI]
public class QueryStringParameterScopeParser : QueryExpressionParser
{
    private readonly FieldChainRequirements _chainRequirements;
    private ResourceType? _resourceTypeInScope;

    public QueryStringParameterScopeParser(FieldChainRequirements chainRequirements)
    {
        _chainRequirements = chainRequirements;
    }

    public QueryStringParameterScopeExpression Parse(string source, ResourceType resourceTypeInScope)
    {
        ArgumentGuard.NotNull(resourceTypeInScope);

        _resourceTypeInScope = resourceTypeInScope;

        Tokenize(source);

        QueryStringParameterScopeExpression expression = ParseQueryStringParameterScope();

        AssertTokenStackIsEmpty();

        return expression;
    }

    protected QueryStringParameterScopeExpression ParseQueryStringParameterScope()
    {
        if (!TokenStack.TryPop(out Token? token) || token.Kind != TokenKind.Text)
        {
            throw new QueryParseException("Parameter name expected.");
        }

        var name = new LiteralConstantExpression(token.Value!);

        ResourceFieldChainExpression? scope = null;

        if (TokenStack.TryPeek(out Token? nextToken) && nextToken.Kind == TokenKind.OpenBracket)
        {
            TokenStack.Pop();

            scope = ParseFieldChain(_chainRequirements, null);

            EatSingleCharacterToken(TokenKind.CloseBracket);
        }

        return new QueryStringParameterScopeExpression(name, scope);
    }

    protected override IImmutableList<ResourceFieldAttribute> OnResolveFieldChain(string path, FieldChainRequirements chainRequirements)
    {
        if (chainRequirements == FieldChainRequirements.EndsInToMany)
        {
            // The mismatch here (ends-in-to-many being interpreted as entire-chain-must-be-to-many) is intentional.
            return ChainResolver.ResolveToManyChain(_resourceTypeInScope!, path);
        }

        if (chainRequirements == FieldChainRequirements.IsRelationship)
        {
            return ChainResolver.ResolveRelationshipChain(_resourceTypeInScope!, path);
        }

        throw new InvalidOperationException($"Unexpected combination of chain requirement flags '{chainRequirements}'.");
    }
}
