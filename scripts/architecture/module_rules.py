"""Logical owners of Chat code. New files in a feature directory inherit its owner."""

from pathlib import PurePosixPath

CHAT_PROJECTS = {"Alycia.Modules.Chat.Application", "Alycia.Modules.Chat.Infrastructure"}

# Root-level code predates the feature directories. A new root file needs an owner.
ROOT_FILES = {
    "Conversations": {"ChatContracts", "ChatService", "ConversationArchive", "EfChatRepository"},
    "Context": {"ContextGuides", "ConversationContext", "FileContext", "ProjectContext", "RecursiveContext",
                "RecursiveSummarizer", "SlidingContext", "TokenBudget"},
    "Inference": {"GenerationGate", "InferenceWindow"},
    "Documents": {"AnalysisCheckpoints", "CodeStructure", "DocumentAnalysisService", "FileImport",
                  "ProjectSourceReader", "StructuredDocuments"},
    "Indexing": {"EmbeddingIndex", "IndexedKnowledge", "SourceIndex"},
    "Retrieval": {"CitationAudit", "ClaimAudit", "SemanticClaimAudit", "EvidenceComposer", "LexicalIndex",
                  "OnnxPassageReranker", "PassageClassification", "ProjectRetrieval", "ProjectRetrievalService",
                  "QueryPlanning", "QueryRouting"},
    "Projects": {"ProjectInventory", "WorkspaceSources"},
    "Configuration": {"ChatOptions", "ConversationConfiguration", "ConversationSettingsTransfer", "ImportSettingsStore"},
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
    if basename == "ConversationSettingsService" and parts[2] == "Context":
        return "Configuration"
    if len(parts) > 3:
        return parts[2] if parts[2] in DIRECTORIES else None
    return next((area for area, names in ROOT_FILES.items() if basename in names), None)

# Direct logical dependencies (including typed ports and records). A target's domain
# is not permission to use its Infrastructure classes; those are restricted below.
ALLOWED = {
    "Configuration": {"Context", "Conversations", "Documents", "Indexing", "Inference", "Memory", "Persistence", "Projects", "Tools"},
    "Context": {"Configuration", "Conversations", "Indexing", "Inference", "Memory", "Retrieval", "Tools"},
    "Conversations": {"Configuration", "Context", "Documents", "Inference", "Memory", "Persistence", "Projects", "Retrieval", "Tools"},
    "Documents": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Memory", "Persistence", "Projects"},
    "Indexing": {"Configuration", "Context", "Conversations", "Documents", "Inference", "Memory", "Persistence", "Projects", "Retrieval"},
    "Inference": {"Configuration", "Context", "Conversations", "Retrieval"},
    "Memory": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Persistence", "Retrieval"},
    "Persistence": set(),
    "Projects": {"Configuration", "Conversations", "Documents", "Inference", "Persistence"},
    "Retrieval": {"Configuration", "Context", "Conversations", "Indexing", "Inference", "Memory", "Tools"},
    "Tools": {"Configuration", "Context", "Conversations", "Documents", "Indexing", "Inference", "Memory", "Persistence", "Projects", "Retrieval"},
}

# Reviewed cross-owner uses within Chat.Infrastructure. Adapters serving different
# owners may share the existing DbContext. Concrete exceptions do not grant access
# to other implementations in the same feature; see the architecture ownership table.
ADAPTERS = {
    "Configuration": {"FileImport", "FileSummary", "WorkspaceSources"},
    "Conversations": {"DocumentAnalysisService", "FileStorage", "WorkspaceSources"},
    "Documents": {"ImportSettingsStore", "EfChatRepository", "WorkspaceSources"},
    "Indexing": {"CodeStructure", "FileStorage", "ProjectSource", "ProjectSourceReader", "StructuredDocuments",
                 "ProjectProfiles", "WorkspaceSources"},
    "Projects": {"EfChatRepository", "FileImport", "FileStorage", "FileSummary", "ProjectSourceReader"},
}

# These consumers intentionally see a specific port or value and no implementation.
NARROW_EDGES = {
    ("Context", "Tools"): {"ITurnTools"},
    ("Configuration", "Inference"): {"IChatModel", "ITextTokenCounter"},
    ("Retrieval", "Conversations"): {"IChatRepository", "IConversationContextReader", "IConversationMessagesReader"},
    ("Retrieval", "Tools"): {"ProjectToolResult"},
    ("Inference", "Conversations"): {"ChatConflictException"},
    ("Inference", "Retrieval"): {"EvidenceText"},
}
