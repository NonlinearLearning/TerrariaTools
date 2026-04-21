using Application.Contracts;
using Application.Contracts.Rewrite;
using Application.Contracts.Rewrite.Artifacts;
using Domain.Rewrite;
using Domain.Rewrite.Artifacts;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static CodeRewriteResultDto Map(CodeRewriteResult result)
    {
        return new CodeRewriteResultDto
        {
            RewriteKind = Map(result.RewriteKind),
            TargetName = result.TargetName,
            SourceCode = result.SourceCode,
            Changed = result.Changed,
            Diagnostics = result.Diagnostics.ToArray(),
        };
    }

    public static MemberSliceDto Map(MemberSlice memberSlice)
    {
        return new MemberSliceDto
        {
            ClassName = memberSlice.ClassName,
            RootMemberName = memberSlice.RootMemberName,
            SourceCode = memberSlice.SourceCode,
            MemberNames = memberSlice.MemberNames.ToArray(),
        };
    }

    public static ShadowClassDto Map(ShadowClass shadowClass)
    {
        return new ShadowClassDto
        {
            ClassName = shadowClass.ClassName,
            ShadowClassName = shadowClass.ShadowClassName,
            SourceCode = shadowClass.SourceCode,
            Boundary = Map(shadowClass.Boundary),
            MemberNames = shadowClass.MemberNames.ToArray(),
            ReferenceMappings = shadowClass.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static RuntimeClosureDto Map(RuntimeClosure runtimeClosure)
    {
        return new RuntimeClosureDto
        {
            ClassName = runtimeClosure.ClassName,
            RootMethodName = runtimeClosure.RootMethodName,
            ClosureClassName = runtimeClosure.ClosureClassName,
            SourceCode = runtimeClosure.SourceCode,
            Boundary = Map(runtimeClosure.Boundary),
            MemberNames = runtimeClosure.MemberNames.ToArray(),
            IntegrityStatus = Map(runtimeClosure.Boundary).IntegrityStatus,
            ReferenceMappings = runtimeClosure.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static ContractCodeRewriteKind Map(CodeRewriteKind rewriteKind)
    {
        return rewriteKind switch
        {
            CodeRewriteKind.DeleteClass => ContractCodeRewriteKind.DeleteClass,
            CodeRewriteKind.DeleteMethod => ContractCodeRewriteKind.DeleteMethod,
            CodeRewriteKind.PrivatizeMethod => ContractCodeRewriteKind.PrivatizeMethod,
            CodeRewriteKind.ClearMethodBody => ContractCodeRewriteKind.ClearMethodBody,
            _ => ContractCodeRewriteKind.Unknown,
        };
    }
}
