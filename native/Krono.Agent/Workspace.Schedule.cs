namespace Krono_Agent;

public sealed partial class Workspace
{
    public sealed record ScheduledTask(string Id, string Name, string Prompt, string Provider, string Model, string Mode, string Permissions, string? Project,
        string Recurrence, string TimeZone, int IntervalMinutes, int ClockMinutes, long NextRun, string State);
    public sealed record ScheduledRun(string Id, ScheduledTask Task, string Chat);
    public bool ScheduleEnabled => Setting("native.scheduleEnabled") == "true";
    static ScheduledTask TaskFrom(System.Text.Json.Nodes.JsonObject row) => new(row["id"]!.ToString(), row["name"]!.ToString(), row["prompt"]!.ToString(), row["provider"]!.ToString(), row["model"]!.ToString(), row["mode"]!.ToString(), row["permissions"]!.ToString(), row["project_id"]?.ToString(), row["recurrence"]!.ToString(), row["time_zone"]!.ToString(), (int)row["interval_minutes"]!.GetValue<long>(), (int)row["clock_minutes"]!.GetValue<long>(), row["next_run"]!.GetValue<long>(), row["state"]!.ToString());
    public List<ScheduledTask> ScheduledTasks() => Query("SELECT * FROM scheduled_tasks WHERE account_id=$p0 AND state<>'cancelled' ORDER BY next_run,id", AccountId).Select(TaskFrom).ToList();
    public void SaveTask(ScheduledTask task)
    {
        if (task.Name.Trim().Length is 0 or > 100 || task.Prompt.Trim().Length is 0 or > 32000 || task.Model.Trim().Length is 0 or > 400 ||
            task.Mode is not ("chat" or "plan" or "agent") || task.Permissions is not ("ask" or "project" or "full") || task.Recurrence is not ("once" or "interval" or "daily") || task.IntervalMinutes is < 1 or > 10080 || task.ClockMinutes is < 0 or >= 1440 || task.NextRun <= 0)
            throw new InvalidOperationException("Provide a title, prompt, model and a valid schedule.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone);
        Execute("INSERT INTO scheduled_tasks(id,account_id,name,prompt,provider,model,mode,permissions,project_id,recurrence,time_zone,interval_minutes,clock_minutes,next_run,state) VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14) ON CONFLICT(id) DO UPDATE SET name=excluded.name,prompt=excluded.prompt,provider=excluded.provider,model=excluded.model,mode=excluded.mode,permissions=excluded.permissions,project_id=excluded.project_id,recurrence=excluded.recurrence,time_zone=excluded.time_zone,interval_minutes=excluded.interval_minutes,clock_minutes=excluded.clock_minutes,next_run=excluded.next_run,state=excluded.state",
            task.Id, AccountId, task.Name.Trim(), task.Prompt.Trim(), task.Provider, task.Model.Trim(), task.Mode, task.Permissions, task.Project, task.Recurrence, task.TimeZone, task.IntervalMinutes, task.ClockMinutes, task.NextRun, task.State);
    }
    public void SetTaskState(string id, string state) {
        if (state is not ("active" or "paused" or "cancelled")) throw new ArgumentException("Invalid schedule state.");
        Execute("UPDATE scheduled_tasks SET state=$p0 WHERE id=$p1 AND account_id=$p2 AND state IN ('active','paused')", state, id, AccountId);
    }
    public static DateTimeOffset ScheduleTime(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A skipped daylight-saving minute advances to the first valid local minute.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
    public static long NextScheduledTime(ScheduledTask task, long now)
    {
        if (task.Recurrence == "once") return task.NextRun;
        if (task.Recurrence == "interval") {
            var period = task.IntervalMinutes * 60000L;
            return task.NextRun > now ? task.NextRun : task.NextRun + ((now - task.NextRun) / period + 1) * period;
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone);
        var date = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), zone).Date;
        var candidate = ScheduleTime(date.AddMinutes(task.ClockMinutes), zone).ToUnixTimeMilliseconds();
        return candidate > now ? candidate : ScheduleTime(date.AddDays(1).AddMinutes(task.ClockMinutes), zone).ToUnixTimeMilliseconds();
    }
    public ScheduledRun? ClaimScheduledRun(long now)
    {
        if (!ScheduleEnabled) return null;
        Execute("BEGIN IMMEDIATE");
        try {
            Execute("UPDATE scheduled_runs SET state='interrupted',finished_at=$p0,error='The app closed before this run finished.' WHERE state='running' AND heartbeat_at<$p1", now, now - 120000);
            var row = Query("SELECT * FROM scheduled_tasks WHERE account_id=$p0 AND state='active' AND next_run<=$p1 AND NOT EXISTS(SELECT 1 FROM scheduled_runs WHERE state='running') ORDER BY next_run,id LIMIT 1", AccountId, now).FirstOrDefault();
            if (row is null) { Execute("COMMIT"); return null; }
            var task = TaskFrom(row);
            var used = Query("SELECT id FROM scheduled_runs WHERE task_id=$p0 AND due_at=$p1", task.Id, task.NextRun).Count > 0;
            Execute("UPDATE scheduled_tasks SET next_run=$p0,state=$p1 WHERE id=$p2", NextScheduledTime(task, now), task.Recurrence == "once" ? "completed" : "active", task.Id);
            if (used) { Execute("COMMIT"); return null; }
            var project = task.Project is not null && Query("SELECT id FROM projects WHERE id=$p0 AND deleted_at IS NULL", task.Project).Count > 0 ? task.Project : null;
            if (task.Project is not null && project is null) {
                Execute("INSERT INTO scheduled_runs(id,task_id,due_at,started_at,heartbeat_at,finished_at,state,error) VALUES($p0,$p1,$p2,$p3,$p3,$p3,'failed','The task project no longer exists.')", Guid.NewGuid().ToString("N"), task.Id, task.NextRun, now);
                Execute("COMMIT"); return null;
            }
            var chat = CreateChat(project); Execute("UPDATE chats SET name=$p0 WHERE id=$p1", "Scheduled · " + task.Name, chat); AddMessage(chat, "user", task.Prompt);
            var run = new ScheduledRun(Guid.NewGuid().ToString("N"), task, chat);
            Execute("INSERT INTO scheduled_runs(id,task_id,due_at,started_at,heartbeat_at,state,chat_id) VALUES($p0,$p1,$p2,$p3,$p3,'running',$p4)", run.Id, task.Id, task.NextRun, now, chat);
            Execute("COMMIT"); return run;
        } catch { Execute("ROLLBACK"); throw; }
    }
    public void PulseScheduledRun(string id) => Execute("UPDATE scheduled_runs SET heartbeat_at=$p0 WHERE id=$p1 AND state='running'", Now, id);
    public void FinishScheduledRun(string id, string state, string? error = null) => Execute("UPDATE scheduled_runs SET state=$p0,finished_at=$p1,error=$p2 WHERE id=$p3 AND state='running'", state, Now, error, id);
    public List<System.Text.Json.Nodes.JsonObject> ScheduledHistory() => Query("SELECT r.*,t.name FROM scheduled_runs r JOIN scheduled_tasks t ON t.id=r.task_id WHERE t.account_id=$p0 ORDER BY r.started_at DESC LIMIT 24", AccountId);
}
