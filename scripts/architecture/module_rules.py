"""Logical owners of Chat code. New files in a feature directory inherit its owner."""

from pathlib import PurePosixPath

CHAT_PROJECTS = {"Alycia.Modules.Chat.Application", "Alycia.Modules.Chat.Infrastructure"}

# Root-level code predates the feature directories. A new root file needs an owner.
ROOT_FILES = {
    "Conversations": {"ChatContracts", "ChatService", "ConversationArchive", "EfChatRepository"},
    "Context": {"ContextGuides", "ConversationContext", "FileContext", "ProjectContext", "RecursiveContext",
                "RecursiveSummarizer", "SlidingContext", "TokenBudget"},
    "Inference": {"GenerationGate", "InferenceWindow"},
    "Documents": {"AnalysisCheckpoints", "DocumentAnalysisService", "FileImport",
                  "ProjectSourceReader", "StructuredDocuments"},
    "Indexing": {"EmbeddingIndex", "IndexedKnowledge", "SourceIndex"},
    "Retrieval": {"CitationAudit", "ClaimAudit", "SemanticClaimAudit", "EvidenceComposer", "LexicalIndex",
                  "OnnxPassageReranker", "PassageClassification", "ProjectRetrieval", "ProjectRetrievalService",
                  "QueryPlanning", "QueryRouting"},
    "Projects": {"ProjectInventory", "WorkspaceSources"},
    "Configuration": {"ChatOptions", "ConversationSettingsTransfer", "ImportSettingsStore"},
    "Persistence": {"ChatDbContext"},
}
DIRECTORIES = {"Conversations", "Context", "Inference", "Documents", "Indexing", "Retrieval",
               "Configuration", "Memory", "Tools", "Projects"}
# Ports/value types co-located with another owner for historical source compatibility.
TYPE_OWNERS = {
    ("TokenBudget", "MeteredChatModel"): "Inference",
}


def owner(path, symbol=""):
    parts = PurePosixPath(path).parts
    if parts[1] not in CHAT_PROJECTS:
        return None
    basename = parts[-1].split(".")[0]
    if (basename, symbol) in TYPE_OWNERS:
        return TYPE_OWNERS[basename, symbol]
    if len(parts) > 3:
        return parts[2] if parts[2] in DIRECTORIES else None
    return next((area for area, names in ROOT_FILES.items() if basename in names), None)

def module_owner(path, symbol):
    parts = PurePosixPath(path).parts
    if len(parts) < 3 or parts[0] != "src":
        return None
    project = parts[1]
    if project in CHAT_PROJECTS:
        logical = owner(path, symbol)
        return "Workspaces" if logical == "Projects" else logical
    if project == "Alycia.Modules.Chat.Domain":
        return {"Attachment": "Documents", "DocumentAnalysis": "Documents", "WorkspaceFileOverride": "Documents",
                "WorkspaceProfile": "Workspaces", "PromptProfile": "Configuration"}.get(symbol, "Conversations")
    if project.startswith("Alycia.Modules.") and project != "Alycia.Modules.Testing":
        return project.split(".")[2]
    return None


# Direct logical dependencies (including typed ports and records). A target's domain
# is not permission to use its Infrastructure classes; those are restricted below.
ALLOWED = {
    "Configuration": {"Context", "Conversations", "Inference", "Memory", "Persistence", "Tools"},
    "Context": {"Configuration", "Conversations", "Indexing", "Inference", "Memory", "Retrieval"},
    "Conversations": {"Configuration", "Context", "Inference", "Memory", "Persistence", "Retrieval", "Tools"},
    "Documents": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Memory", "Persistence", "Projects"},
    "Indexing": {"Configuration", "Context", "Conversations", "Inference", "Memory", "Persistence", "Retrieval"},
    "Inference": {"Configuration", "Context"},
    "Memory": {"Configuration", "Context", "Conversations", "Indexing", "Retrieval"},
    "Persistence": set(),
    "Projects": set(),
    "Retrieval": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Memory"},
    "Tools": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Memory", "Retrieval"},
}

# These consumers intentionally see a specific port or value and no implementation.
NARROW_EDGES = {
    ("Configuration", "Inference"): {"IChatModel", "ITextTokenCounter"},
    ("Context", "Conversations"): {"IConversationContextReader", "IConversationMessagesReader"},
    ("Context", "Tools"): {"ITurnTools"},
    ("Conversations", "Tools"): {"IProjectTools", "ITurnTools"},
    ("Indexing", "Inference"): {"IChatModel"},
    ("Memory", "Conversations"): {"IConversationContextReader", "IConversationMessagesReader"},
    ("Retrieval", "Conversations"): {"IChatRepository", "IConversationContextReader", "IConversationMessagesReader"},
    ("Retrieval", "Indexing"): {"IIndexedEmbeddings"},
    ("Tools", "Indexing"): {"IIndexedEmbeddings"},
}

# These consumers use request snapshots. Stored settings and tracked chat rows belong at the owning adapter.
SNAPSHOT_CONSUMERS = {"Context", "Documents", "Indexing", "Inference", "Memory", "Retrieval", "Tools"}
OWNER_INPUT_TYPES = {
    "T:Alycia.Contracts.Chat.ChatSettings",
    "T:Alycia.Modules.Chat.Application.ChatOptions",
    "T:Alycia.Modules.Chat.Domain.Conversation",
    "T:Alycia.Modules.Chat.Domain.ChatMessage",
    "T:Alycia.Modules.Chat.Domain.Attachment",
    "T:Alycia.Modules.Chat.Domain.WorkspaceProfile",
}

# Contract namespaces accepted by already isolated assemblies. Chat DTOs retain their
# historical namespace for transport compatibility; no module implementation is a contract.
CONTRACT_AREAS = {
    "Alycia.Modules.Documents": {"Chat", "Documents", "Indexing"},
    "Alycia.Modules.Indexing": {"Chat", "Indexing", "Inference", "Workspaces"},
    "Alycia.Modules.Tools": {"Chat", "Documents", "Indexing", "Tools"},
    "Alycia.Modules.Context": {"Context"},
    "Alycia.Modules.Memory": {"Indexing", "Chat", "Memory"},
    "Alycia.Modules.Inference": {"Chat", "Inference"},
    "Alycia.Modules.Retrieval": {"Indexing", "Retrieval", "Chat", "Documents"},
    "Alycia.Modules.Retrieval.Onnx": {"Indexing", "Retrieval"},
    "Alycia.Modules.Testing": {"Chat", "Indexing", "Inference", "Documents", "Workspaces", "Retrieval"},
    "Alycia.Modules.Workspaces.Infrastructure": {"Workspaces", "Chat", "Indexing"},
}
