using Microsoft.Data.Sqlite;

namespace MinimalRoslynCpg.Persistence.Sqlite;

internal static class SqliteCpgShardSchema
{
  internal const int Version = 6;

  internal static async Task EnsureCreatedAsync(SqliteConnection connection, CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.CommandText = """
      CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);
      CREATE TABLE IF NOT EXISTS projects (
        project_id TEXT PRIMARY KEY,
        root_path TEXT NOT NULL,
        schema_version INTEGER NOT NULL,
        builder_profile_hash TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS files (
        file_id TEXT PRIMARY KEY,
        project_id TEXT NOT NULL,
        relative_path TEXT NOT NULL,
        source_hash TEXT NOT NULL,
        status TEXT NOT NULL,
        UNIQUE(project_id, relative_path, source_hash));
      CREATE TABLE IF NOT EXISTS fragments (
        fragment_id TEXT PRIMARY KEY,
        file_id TEXT NOT NULL,
        fragment_kind TEXT NOT NULL,
        span_start INTEGER NOT NULL,
        span_length INTEGER NOT NULL,
        fragment_hash TEXT NOT NULL,
        status TEXT NOT NULL,
        UNIQUE(file_id, fragment_kind, span_start, span_length, fragment_hash));
      CREATE TABLE IF NOT EXISTS shards (
        shard_id TEXT PRIMARY KEY,
        fragment_id TEXT NOT NULL,
        shard_path TEXT NOT NULL,
        byte_length INTEGER NOT NULL,
        shard_hash TEXT NOT NULL,
        status TEXT NOT NULL,
        completed_at_utc TEXT NULL,
        UNIQUE(fragment_id, shard_hash));
      CREATE TABLE IF NOT EXISTS node_locations (
        node_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        local_offset INTEGER NOT NULL,
        PRIMARY KEY(node_id, shard_id));
      CREATE TABLE IF NOT EXISTS symbol_locations (
        symbol_key TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        local_offset INTEGER NOT NULL,
        PRIMARY KEY(symbol_key, shard_id, local_offset));
      CREATE TABLE IF NOT EXISTS span_locations (
        file_id TEXT NOT NULL,
        span_start INTEGER NOT NULL,
        span_length INTEGER NOT NULL,
        shard_id TEXT NOT NULL,
        PRIMARY KEY(file_id, span_start, span_length, shard_id));
      CREATE INDEX IF NOT EXISTS ix_files_project_path ON files(project_id, relative_path);
      CREATE INDEX IF NOT EXISTS ix_fragments_file_status ON fragments(file_id, status);
      CREATE INDEX IF NOT EXISTS ix_shards_fragment_status ON shards(fragment_id, status);
      CREATE INDEX IF NOT EXISTS ix_symbol_locations_key ON symbol_locations(symbol_key);
      CREATE INDEX IF NOT EXISTS ix_span_locations_file_span ON span_locations(file_id, span_start, span_length);
      CREATE TABLE IF NOT EXISTS build_sessions (
        build_id TEXT PRIMARY KEY,
        status TEXT NOT NULL,
        created_at_utc TEXT NOT NULL,
        completed_at_utc TEXT NULL);
      CREATE TABLE IF NOT EXISTS session_files (
        build_id TEXT NOT NULL,
        file_id TEXT NOT NULL,
        project_id TEXT NOT NULL,
        relative_path TEXT NOT NULL,
        source_hash TEXT NOT NULL,
        schema_version INTEGER NOT NULL,
        builder_profile_hash TEXT NOT NULL,
        PRIMARY KEY(build_id, file_id));
      CREATE TABLE IF NOT EXISTS session_fragments (
        build_id TEXT NOT NULL,
        fragment_id TEXT NOT NULL,
        file_id TEXT NOT NULL,
        fragment_kind TEXT NOT NULL,
        span_start INTEGER NOT NULL,
        span_length INTEGER NOT NULL,
        fragment_hash TEXT NOT NULL,
        PRIMARY KEY(build_id, fragment_id));
      CREATE TABLE IF NOT EXISTS session_shards (
        build_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        fragment_id TEXT NOT NULL,
        shard_path TEXT NOT NULL,
        byte_length INTEGER NOT NULL,
        shard_hash TEXT NOT NULL,
        PRIMARY KEY(build_id, shard_id));
      CREATE TABLE IF NOT EXISTS session_node_locations (
        build_id TEXT NOT NULL,
        node_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        local_offset INTEGER NOT NULL,
        PRIMARY KEY(build_id, node_id, shard_id));
      CREATE TABLE IF NOT EXISTS session_fragment_owners (
        build_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        owner_fragment_id TEXT NOT NULL,
        PRIMARY KEY(build_id, shard_id));
      CREATE TABLE IF NOT EXISTS session_fragment_adjacencies (
        build_id TEXT NOT NULL,
        owner_fragment_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        direction TEXT NOT NULL,
        PRIMARY KEY(build_id, owner_fragment_id, shard_id));
      CREATE TABLE IF NOT EXISTS session_boundary_node_locations (
        build_id TEXT NOT NULL,
        node_id TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        PRIMARY KEY(build_id, node_id, shard_id));
      CREATE TABLE IF NOT EXISTS session_symbol_locations (
        build_id TEXT NOT NULL,
        symbol_key TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        local_offset INTEGER NOT NULL,
        PRIMARY KEY(build_id, symbol_key, shard_id, local_offset));
      CREATE TABLE IF NOT EXISTS session_span_locations (
        build_id TEXT NOT NULL,
        file_id TEXT NOT NULL,
        span_start INTEGER NOT NULL,
        span_length INTEGER NOT NULL,
        shard_id TEXT NOT NULL,
        PRIMARY KEY(build_id, file_id, span_start, span_length, shard_id));
      CREATE TABLE IF NOT EXISTS session_reusable_fragments (
        build_id TEXT NOT NULL,
        project_id TEXT NOT NULL,
        relative_path TEXT NOT NULL,
        schema_version INTEGER NOT NULL,
        builder_profile_hash TEXT NOT NULL,
        fragment_kind TEXT NOT NULL,
        span_start INTEGER NOT NULL,
        span_length INTEGER NOT NULL,
        fragment_hash TEXT NOT NULL,
        node_id_fingerprint TEXT NOT NULL,
        shard_id TEXT NOT NULL,
        PRIMARY KEY(build_id, project_id, relative_path, schema_version, builder_profile_hash, fragment_kind, span_start, span_length, fragment_hash, node_id_fingerprint));
      CREATE TABLE IF NOT EXISTS session_manifests (
        build_id TEXT PRIMARY KEY,
        completed_at_utc TEXT NOT NULL,
        shard_count INTEGER NOT NULL,
        shard_bytes INTEGER NOT NULL);
      CREATE TABLE IF NOT EXISTS session_routing_indexes (
        build_id TEXT PRIMARY KEY,
        relative_path TEXT NOT NULL,
        format_version INTEGER NOT NULL,
        byte_length INTEGER NOT NULL,
        payload_hash TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS physical_shard_references (
        shard_path TEXT PRIMARY KEY,
        shard_hash TEXT NOT NULL,
        byte_length INTEGER NOT NULL,
        reference_count INTEGER NOT NULL);
      CREATE INDEX IF NOT EXISTS ix_session_files_identity ON session_files(project_id, relative_path, source_hash, schema_version, builder_profile_hash);
      CREATE INDEX IF NOT EXISTS ix_session_fragments_file ON session_fragments(build_id, file_id);
      CREATE INDEX IF NOT EXISTS ix_session_nodes_node ON session_node_locations(node_id);
      CREATE INDEX IF NOT EXISTS ix_session_fragment_adjacencies_owner ON session_fragment_adjacencies(build_id, owner_fragment_id);
      CREATE INDEX IF NOT EXISTS ix_session_boundary_nodes_node ON session_boundary_node_locations(node_id);
      CREATE INDEX IF NOT EXISTS ix_session_symbols_symbol ON session_symbol_locations(symbol_key);
      CREATE INDEX IF NOT EXISTS ix_session_spans_file_span ON session_span_locations(file_id, span_start, span_length);
      CREATE INDEX IF NOT EXISTS ix_session_reusable_fragments_lookup ON session_reusable_fragments(project_id, relative_path, schema_version, builder_profile_hash, fragment_kind, span_start, span_length, fragment_hash, node_id_fingerprint);
      CREATE INDEX IF NOT EXISTS ix_session_routing_indexes_build ON session_routing_indexes(build_id);
      CREATE INDEX IF NOT EXISTS ix_physical_shard_references_count ON physical_shard_references(reference_count);
      """;
    await command.ExecuteNonQueryAsync(cancellationToken);

    await using var readVersion = connection.CreateCommand();
    readVersion.CommandText = "SELECT version FROM schema_info LIMIT 1;";
    var currentVersion = await readVersion.ExecuteScalarAsync(cancellationToken);
    if (currentVersion is null)
    {
      await using var writeVersion = connection.CreateCommand();
      writeVersion.CommandText = "INSERT INTO schema_info(version) VALUES ($version);";
      writeVersion.Parameters.AddWithValue("$version", Version);
      await writeVersion.ExecuteNonQueryAsync(cancellationToken);
      return;
    }

    var version = Convert.ToInt32(currentVersion, System.Globalization.CultureInfo.InvariantCulture);
    if (version > Version)
    {
      throw new InvalidOperationException("The CPG shard catalog schema version is unsupported.");
    }

    if (version < Version)
    {
      await using var writeVersion = connection.CreateCommand();
      writeVersion.CommandText = "UPDATE schema_info SET version = $version;";
      writeVersion.Parameters.AddWithValue("$version", Version);
      await writeVersion.ExecuteNonQueryAsync(cancellationToken);
    }
  }
}
