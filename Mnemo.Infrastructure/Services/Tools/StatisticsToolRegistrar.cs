using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools.Statistics;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>
/// Registers statistics-related AI/extension tools with the function registry. The tool surface
/// covers the core CRUD + exists + read-fields operations the user requested, plus increments,
/// query, and schema discovery for richer extension scenarios.
/// </summary>
public static class StatisticsToolRegistrar
{
    public static void Register(IFunctionRegistry registry, StatisticsToolService svc)
    {
        registry.RegisterTool(new AIToolDefinition(
            "stats_create_record",
            "Create a new statistics record at (namespace, kind, key) with typed fields and optional JSON metadata. Fails if it already exists.",
            typeof(StatsWriteRecordParameters),
            async args => await svc.CreateRecordAsync((StatsWriteRecordParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_update_record",
            "Update an existing statistics record. merge_mode: \"merge\" (default) keeps existing fields; \"replace\" overwrites the field bag. Optional expected_version enforces optimistic concurrency.",
            typeof(StatsWriteRecordParameters),
            async args => await svc.UpdateRecordAsync((StatsWriteRecordParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_upsert_record",
            "Create or update a statistics record. Use when existence is irrelevant to the caller.",
            typeof(StatsWriteRecordParameters),
            async args => await svc.UpsertRecordAsync((StatsWriteRecordParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_record_exists",
            "Returns whether a statistics record exists at (namespace, kind, key).",
            typeof(StatsRecordIdentityParameters),
            async args => await svc.ExistsAsync((StatsRecordIdentityParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_get_record",
            "Returns the full statistics record (envelope + all fields + metadata).",
            typeof(StatsRecordIdentityParameters),
            async args => await svc.GetRecordAsync((StatsRecordIdentityParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_read_fields",
            "Returns only the requested fields from a statistics record.",
            typeof(StatsReadFieldsParameters),
            async args => await svc.ReadFieldsAsync((StatsReadFieldsParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_increment_field",
            "Atomically increments an Integer field on a record (creates the record if missing).",
            typeof(StatsIncrementFieldParameters),
            async args => await svc.IncrementFieldAsync((StatsIncrementFieldParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_delete_record",
            "Deletes a statistics record. Idempotent.",
            typeof(StatsRecordIdentityParameters),
            async args => await svc.DeleteRecordAsync((StatsRecordIdentityParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_query",
            "Queries records by namespace (required), optional kind, optional key prefix, and optional updated_after/updated_before ISO timestamps.",
            typeof(StatsQueryParameters),
            async args => await svc.QueryAsync((StatsQueryParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition(
            "stats_list_schemas",
            "Lists registered statistics schemas, optionally filtered by namespace.",
            typeof(StatsListSchemasParameters),
            async args => await svc.ListSchemasAsync((StatsListSchemasParameters)args).ConfigureAwait(false)));
    }
}
