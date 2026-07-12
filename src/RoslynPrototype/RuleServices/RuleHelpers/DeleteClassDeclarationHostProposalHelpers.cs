using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Decision;

public static class DeleteClassDeclarationHostProposalHelpers
{
    public static IEnumerable<DeclarationHostPayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, DeclarationHostKind kind)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Payload is not DeclarationHostPayload payload ||
                payload.Kind != kind)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.HostDeclaration);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }
}
