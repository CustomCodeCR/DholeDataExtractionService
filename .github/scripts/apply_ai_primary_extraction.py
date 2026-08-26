from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)


service = Path("src/Dhole.DataExtraction.Infrastructure/Pipeline/AutomatedPricingExtractionService.cs")
text = service.read_text()

# PrepareAiRequestAsync: AI receives the source evidence, not Config hints or a
# deterministic draft. DataExtraction remains the canonicalization authority.
pattern = re.compile(
    r"(?P<indent>\s*)var catalogHints = await BuildCatalogHintsAsync\(\s*"
    r"deterministicResponse,\s*BuildCatalogSearchContent\(\s*normalizedSubject,\s*"
    r"context\.BodyText,\s*context\.BodyHtml,\s*limitedSourceContent\s*\),\s*"
    r"cancellationToken\s*\);",
    re.MULTILINE,
)
replacement = (
    "\n        // AI extracts semantic facts from the original evidence. Catalog resolution,\n"
    "        // canonical names and business validation belong exclusively to DataExtraction.\n"
    "        var rawExtractionHints = Array.Empty<AiCatalogGroupHint>();"
)
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit("PrepareAiRequestAsync catalog hint block not found")

# ExtractAsync: same rule for the synchronous/manual path.
pattern = re.compile(
    r"(?P<indent>\s*)var catalogHints = await BuildCatalogHintsAsync\(\s*"
    r"deterministicResponse,\s*BuildCatalogSearchContent\(\s*normalizedSubject,\s*"
    r"context\?\.BodyText \?\? request\.SourceEmailBodyText,\s*"
    r"context\?\.BodyHtml \?\? request\.SourceEmailBodyHtml,\s*sourceContent\s*\),\s*"
    r"cancellationToken\s*\);",
    re.MULTILINE,
)
replacement = (
    "\n            // Keep the AI contract source-oriented. DataExtraction resolves Config\n"
    "            // catalogs only after the model has returned the extracted facts.\n"
    "            var rawExtractionHints = Array.Empty<AiCatalogGroupHint>();"
)
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit("ExtractAsync catalog hint block not found")

# Both AI request constructors contain the same three semantic-draft arguments but
# at different indentation levels. Replace them structurally and preserve indent.
pattern = re.compile(
    r"(?P<indent>^[ \t]*)BuildPreviousRows\(deterministicResponse\),\n"
    r"(?P=indent)BuildPreviousIssues\(deterministicResponse\),\n"
    r"(?P=indent)catalogHints,",
    re.MULTILINE,
)

def raw_args(match: re.Match) -> str:
    indent = match.group("indent")
    return (
        f"{indent}Array.Empty<AiPricingEmailRow>(),\n"
        f"{indent}Array.Empty<AiPreviousExtractionIssue>(),\n"
        f"{indent}rawExtractionHints,"
    )

text, count = pattern.subn(raw_args, text)
if count != 2:
    raise SystemExit(f"AI request arguments: expected 2 blocks, found {count}")
service.write_text(text)

worker = Path("src/Dhole.DataExtraction.Workers/Workers/EmailExtractionWorker.cs")
text = worker.read_text()
text = replace_once(
    text,
    '                configuration["AI:AutomaticExtraction:ForceAiForEmail"],\n                false',
    '                configuration["AI:AutomaticExtraction:ForceAiForEmail"],\n                true',
    "ForceAiForEmail default",
)
worker.write_text(text)

settings = Path("src/Dhole.DataExtraction.Workers/appsettings.json")
text = settings.read_text()
for old, new in {
    '"AnalyzeEverySource": false': '"AnalyzeEverySource": true',
    '"PreferAiResult": false': '"PreferAiResult": true',
    '"RequireAiResult": false': '"RequireAiResult": true',
    '"ForceAiForEmail": false': '"ForceAiForEmail": true',
    '"BypassAiWhenDeterministicRowsExist": true': '"BypassAiWhenDeterministicRowsExist": false',
}.items():
    text = replace_once(text, old, new, old)
settings.write_text(text)
