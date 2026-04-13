// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Infrastructure.Persistence.Sqlite.Migrations;

/// <summary>
/// 迁移 005：创建下载断点表。存储分块下载的断点续传数据。
/// </summary>
internal sealed class Migration_005_DownloadCheckpoints : IMigration
{
    public int Version => 5;
    public string Description => "创建下载断点表";
    public string Sql => """
        CREATE TABLE IF NOT EXISTS download_checkpoints (
            task_id         TEXT    PRIMARY KEY,
            manifest_json   TEXT    NOT NULL DEFAULT '',
            saved_at        TEXT    NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS chunk_checkpoints (
            task_id         TEXT    NOT NULL,
            chunk_index     INTEGER NOT NULL,
            range_start     INTEGER NOT NULL,
            range_end       INTEGER NOT NULL,
            downloaded_bytes INTEGER NOT NULL DEFAULT 0,
            is_completed    INTEGER NOT NULL DEFAULT 0,
            partial_file    TEXT,
            hash            TEXT,
            PRIMARY KEY (task_id, chunk_index),
            FOREIGN KEY (task_id) REFERENCES download_checkpoints(task_id) ON DELETE CASCADE
        );
        """;
}
