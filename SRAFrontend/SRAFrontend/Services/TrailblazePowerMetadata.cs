using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SRAFrontend.Data;
using SRAFrontend.Models;
using Tomlyn;
using Tomlyn.Model;

namespace SRAFrontend.Services;

public static class TrailblazePowerMetadata
{
    public static TpTask[] LoadFromDisk()
    {
        var path = Path.Combine(DataPath.AppRoot, "tasks", "config", "trailblaze_power.toml");
        if (!File.Exists(path)) return [];

        try
        {
            var document = Toml.ToModel(File.ReadAllText(path));
            if (document["subtasks"] is not TomlTable subtasks) return [];

            return subtasks.Values
                .OfType<TomlTable>()
                .Select(ParseTask)
                .Where(task => task is not null)
                .Cast<TpTask>()
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static TpTask? ParseTask(TomlTable task)
    {
        if (!TryGetString(task, "func", out var id) ||
            !TryGetString(task, "name", out var name) ||
            !TryGetInt(task, "cost", out var cost) ||
            !TryGetInt(task, "max_count", out var maxSingleTimes) ||
            task["levels"] is not IEnumerable levels)
            return null;

        var parsedLevels = levels
            .OfType<TomlTable>()
            .Select(ParseLevel)
            .Where(level => level is not null)
            .Cast<TpTaskLevel>()
            .ToArray();

        return new TpTask(id, name, parsedLevels, cost, maxSingleTimes);
    }

    private static TpTaskLevel? ParseLevel(TomlTable level)
    {
        if (!TryGetInt(level, "id", out var id) ||
            !TryGetString(level, "name", out var name) ||
            !TryGetString(level, "result", out var result))
            return null;

        return new TpTaskLevel(id, name, result);
    }

    private static bool TryGetString(TomlTable table, string key, out string value)
    {
        if (table.TryGetValue(key, out var raw) && raw is string text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt(TomlTable table, string key, out int value)
    {
        if (table.TryGetValue(key, out var raw))
        {
            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                // Invalid metadata falls back to the backend implementation.
            }
        }

        value = 0;
        return false;
    }
}
