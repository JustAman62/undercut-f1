using Spectre.Console;
using UndercutF1.Data;

namespace UndercutF1.Console;

public static partial class CommandHandler
{
    public static async Task ImportSession(
        int year,
        int? meetingKey,
        int? sessionKey,
        DirectoryInfo? dataDirectory,
        DirectoryInfo? logDirectory,
        bool? isVerbose
    )
    {
        var builder = GetBuilder(
            dataDirectory: dataDirectory,
            logDirectory: logDirectory,
            isVerbose: isVerbose,
            useConsoleLogging: true
        );
        var app = builder.Build();
        var importer = app.Services.GetRequiredService<IDataImporter>();
        var formula1Account = app.Services.GetRequiredService<Formula1Account>();

        if (formula1Account.IsAuthenticated != Formula1Account.AuthenticationResult.Success)
        {
            AnsiConsole.Write(
                new Text(
                    $"""
                    Importing sessions now requires logging in with your F1TV/Formula1 account.
                    Please authenticate first by running 'undercutf1 login'.

                    Tokens expire after a week, so even if you've logged in previously you may need to login again now.

                    Current token status: {formula1Account.IsAuthenticated}
                    """,
                    Color.Red
                )
            );
            return;
        }

        if (!meetingKey.HasValue)
        {
            var res = await importer.GetMeetingsAsync(year);
            WriteMeetings(res.Meetings);
        }
        else if (!sessionKey.HasValue)
        {
            var res = await importer.GetMeetingsAsync(year);
            var meeting = res.Meetings.SingleOrDefault(x => x.Key == meetingKey);
            if (meeting is null)
            {
                AnsiConsole.Write(
                    new Text(
                        $"Failed to find a meeting with the provided key {meetingKey}{Environment.NewLine}",
                        Color.Red
                    )
                );
                WriteMeetings(res.Meetings);
                return;
            }

            await Terminal.OutLineAsync(
                $"Found {meeting.Sessions.Count} sessions inside meeting {meetingKey} {meeting.Name}"
            );
            WriteSessions(meeting);
        }
        else
        {
            await importer.ImportSessionAsync(year, meetingKey.Value, sessionKey.Value);
        }
    }

    private static void WriteMeetings(List<ListMeetingsApiResponse.Meeting> meetings)
    {
        var table = new Table().AddColumns(
            new TableColumn("Key"),
            new("Meeting Name"),
            new("Location")
        );

        table.Title = new TableTitle("Available Meetings");

        foreach (var meeting in meetings)
        {
            table.AddRow(meeting.Key.ToString(), meeting.Name, meeting.Location);
        }

        AnsiConsole.Write(table);
    }

    private static void WriteSessions(ListMeetingsApiResponse.Meeting meeting)
    {
        var table = new Table().AddColumns(
            new TableColumn("Key"),
            new("Meeting Name"),
            new("Session Name"),
            new("Session Start (UTC)")
        );

        table.Title = new TableTitle("Available Sessions");

        foreach (var session in meeting.Sessions)
        {
            table.AddRow(
                session.Key.ToString(),
                meeting.Name,
                session.Name,
                $"{session.StartDate - session.GmtOffset:u}"
            );
        }

        AnsiConsole.Write(table);
    }
}
