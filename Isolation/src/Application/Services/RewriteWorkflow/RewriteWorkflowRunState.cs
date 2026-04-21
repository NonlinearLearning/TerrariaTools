using Application.Contracts.Workflow;
using Logic.Decision;
using Logic.Propagation;
using Logic.Workflow;

namespace Application.Services.RewriteWorkflow;

internal sealed record RewriteWorkflowRunState(
    RunRewriteWorkflowRequest Request,
    Guid RunCorrelationId,
    PropagationResolution Propagation,
    RewriteDecisionResolution Decision,
    RewriteWorkflowArtifacts Artifacts);
