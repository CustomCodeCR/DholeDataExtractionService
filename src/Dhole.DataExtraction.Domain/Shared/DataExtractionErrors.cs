using CustomCodeFramework.Core.Results;

namespace Dhole.DataExtraction.Domain.Shared;

public static class DataExtractionErrors
{
    public static readonly Error ExtractionExecutionNotFound = new(
        "DataExtraction.ExtractionExecutionNotFound",
        "No se encontró la ejecución de extracción solicitada."
    );

    public static readonly Error ExtractionExecutionAlreadyCompleted = new(
        "DataExtraction.ExtractionExecutionAlreadyCompleted",
        "La ejecución de extracción ya fue completada."
    );

    public static readonly Error ExtractionExecutionAlreadyFailed = new(
        "DataExtraction.ExtractionExecutionAlreadyFailed",
        "La ejecución de extracción ya falló."
    );

    public static readonly Error ExtractionExecutionCannotBeProcessed = new(
        "DataExtraction.ExtractionExecutionCannotBeProcessed",
        "La ejecución de extracción no puede ser procesada en su estado actual."
    );

    public static readonly Error SourceDocumentNotFound = new(
        "DataExtraction.SourceDocumentNotFound",
        "No se encontró el documento fuente solicitado."
    );

    public static readonly Error SourceDocumentFileNameRequired = new(
        "DataExtraction.SourceDocumentFileNameRequired",
        "El nombre del archivo es obligatorio."
    );

    public static readonly Error SourceDocumentFileHashRequired = new(
        "DataExtraction.SourceDocumentFileHashRequired",
        "El hash del archivo es obligatorio."
    );

    public static readonly Error UnsupportedFileType = new(
        "DataExtraction.UnsupportedFileType",
        "El tipo de archivo no es soportado. Se permite PDF, Excel, CSV o correo/HTML."
    );

    public static readonly Error EmptyFile = new(
        "DataExtraction.EmptyFile",
        "El archivo está vacío."
    );

    public static readonly Error FileTooLarge = new(
        "DataExtraction.FileTooLarge",
        "El archivo supera el tamaño máximo permitido."
    );

    public static readonly Error InvalidTarget = new(
        "DataExtraction.InvalidTarget",
        "El objetivo de extracción no es válido."
    );

    public static readonly Error InvalidSource = new(
        "DataExtraction.InvalidSource",
        "La fuente de extracción no es válida."
    );

    public static readonly Error PricingExtractionRecordNotFound = new(
        "DataExtraction.PricingExtractionRecordNotFound",
        "No se encontró el registro extraído solicitado."
    );

    public static readonly Error ExtractionIssueNotFound = new(
        "DataExtraction.ExtractionIssueNotFound",
        "No se encontró la incidencia de extracción solicitada."
    );

    public static readonly Error ColumnMappingProfileNotFound = new(
        "DataExtraction.ColumnMappingProfileNotFound",
        "No se encontró el perfil de mapeo de columnas solicitado."
    );

    public static readonly Error ColumnMappingProfileCodeRequired = new(
        "DataExtraction.ColumnMappingProfileCodeRequired",
        "El código del perfil de mapeo es obligatorio."
    );

    public static readonly Error ColumnMappingProfileNameRequired = new(
        "DataExtraction.ColumnMappingProfileNameRequired",
        "El nombre del perfil de mapeo es obligatorio."
    );

    public static readonly Error ColumnMappingProfileCodeAlreadyExists = new(
        "DataExtraction.ColumnMappingProfileCodeAlreadyExists",
        "Ya existe un perfil de mapeo con el mismo código."
    );

    public static readonly Error SystemColumnMappingProfileCannotBeDeleted = new(
        "DataExtraction.SystemColumnMappingProfileCannotBeDeleted",
        "Los perfiles de mapeo del sistema no pueden eliminarse."
    );

    public static readonly Error ColumnMappingRuleNotFound = new(
        "DataExtraction.ColumnMappingRuleNotFound",
        "No se encontró la regla de mapeo solicitada."
    );

    public static readonly Error ColumnMappingRuleSourceColumnRequired = new(
        "DataExtraction.ColumnMappingRuleSourceColumnRequired",
        "El nombre de la columna origen es obligatorio."
    );

    public static readonly Error ColumnMappingRuleTargetFieldRequired = new(
        "DataExtraction.ColumnMappingRuleTargetFieldRequired",
        "El campo destino de la regla de mapeo es obligatorio."
    );

    public static readonly Error InvalidRawJson = new(
        "DataExtraction.InvalidRawJson",
        "El JSON crudo de extracción no tiene un formato válido."
    );

    public static readonly Error InvalidMetadataJson = new(
        "DataExtraction.InvalidMetadataJson",
        "La metadata debe tener formato JSON válido."
    );
}
