using System.Globalization;
using System.Text.Json;

namespace Schoology.WinUI;

internal sealed class ClassWireAISchoologyTools
{
    private readonly SchoologyApiClient _api;
    private readonly SchoologyAuthService.Session _session;
    private readonly ClassWireAIPageContext _context;
    private long LearningUserId => _context.LearningUserId > 0 ? _context.LearningUserId : _session.UserId;
    private bool IsParentLearningContext => LearningUserId != _session.UserId;

    internal ClassWireAISchoologyTools(SchoologyApiClient api, SchoologyAuthService.Session session, ClassWireAIPageContext context)
    { _api = api; _session = session; _context = context; }

    private static JsonElement Schema(string json)
    { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    internal static IReadOnlyList<ClassWireAIToolDefinition> Definitions { get; } =
    [
        new("get_courses", "List the active learning user's Schoology courses. In a parent account, this means the selected student.", Schema("""{"type":"object","properties":{}}""")),
        new("get_groups", "List the active learning user's Schoology groups. In a parent account, this means the selected student.", Schema("""{"type":"object","properties":{}}""")),
        new("get_pending_requests", "List friend requests and group/course invitations for the signed-in account. These are account-level requests, not the selected student's requests.", Schema("""{"type":"object","properties":{}}""")),
        new("respond_to_request", "PROPOSE accepting or declining a verified friend request or group/course invitation for the signed-in account. Use an exact type and ID from get_pending_requests; confirmation is required.", Schema("""{"type":"object","properties":{"type":{"type":"string","enum":["friend","group","section"]},"id":{"type":"integer"},"accept":{"type":"boolean"}},"required":["type","id","accept"]}""")),
        new("get_upcoming_events", "List upcoming Schoology events and assignment due dates. days defaults to 30 and is limited to 120.", Schema("""{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":120}}}""")),
        new("search_assignments", "Search assignment titles and descriptions across enrolled courses or within course_id.", Schema("""{"type":"object","properties":{"query":{"type":"string"},"course_id":{"type":"integer"}},"required":["query"]}""")),
        new("get_course_grades", "Read grade items, grading periods, categories, section grades, and the active learning user's grades for a course. Optionally provide course_id.", Schema("""{"type":"object","properties":{"course_id":{"type":"integer"}}}""")),
        new("get_course_materials", "List materials in a course folder. Open a course first or provide course_id; folder_id defaults to zero.", Schema("""{"type":"object","properties":{"course_id":{"type":"integer"},"folder_id":{"type":"integer"}}}""")),
        new("get_course_updates", "List recent updates in a course. Open a course first or provide course_id.", Schema("""{"type":"object","properties":{"course_id":{"type":"integer"}}}""")),
        new("search_message_recipients", "Search Schoology message recipients and return verified IDs and names. Always call this before send_message; never guess IDs.", Schema("""{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""")),
        new("search_messages", "Search recent Schoology inbox and sent messages by subject or body. Returns sender/recipient IDs, timestamps, unread status and attachment metadata when available.", Schema("""{"type":"object","properties":{"query":{"type":"string"},"folder":{"type":"string","enum":["inbox","sent","both"]},"limit":{"type":"integer","minimum":1,"maximum":60}},"required":["query"]}""")),
        new("get_message_thread", "Retrieve message details from inbox, sent, or both folders by message ID, including attachment metadata and participant identities where available.", Schema("""{"type":"object","properties":{"message_id":{"type":"integer"},"folder":{"type":"string","enum":["inbox","sent","both"]}},"required":["message_id"]}""")),
        new("get_current_assignment", "Read full details and the signed-in user's submission/revision history for the currently open assignment.", Schema("""{"type":"object","properties":{}}""")),
        new("get_assignment_submission", "Read revisions and attachments for a submission. submission_id is the Schoology grade-item/submission path ID, not an arbitrary assignment ID. user_id defaults to the signed-in user.", Schema("""{"type":"object","properties":{"section_id":{"type":"integer"},"submission_id":{"type":"integer"},"user_id":{"type":"integer"}},"required":["section_id","submission_id"]}""")),
        new("get_current_page", "Read the current ClassWire page and selected course/group/assignment context.", Schema("""{"type":"object","properties":{}}""")),
        new("navigate_to_schoology_item", "Navigate within ClassWire to a course, group, assignment, or Messages. This does not change Schoology data.", Schema("""{"type":"object","properties":{"kind":{"type":"string","enum":["course","group","assignment","messages"]},"id":{"type":"integer"},"section_id":{"type":"integer"}},"required":["kind"]}""")),
        new("submit_assignment_files", "PROPOSE submission of files attached to the latest user message to the currently open assignment. Only use when explicitly requested; confirmation is required.", Schema("""{"type":"object","properties":{}}""")),
        new("generate_ai_image", "Generate an image with Google Gemini from the prompt and optional image references. Only use when Gemini is selected and the user asks to generate an image.", Schema("""{"type":"object","properties":{"prompt":{"type":"string"}},"required":["prompt"]}""")),
        new("submit_assignment_text", "PROPOSE a rich-text response for the currently open assignment. The app will show a preview and require explicit confirmation before submission.", Schema("""{"type":"object","properties":{"markdown":{"type":"string"},"text":{"type":"string"},"assignment_id":{"type":"integer"},"section_id":{"type":"integer"}},"required":["markdown"]}""")),
        new("send_message", "PROPOSE a Schoology message using the exact recipient ID returned by search_message_recipients. Explicit confirmation is required before sending.", Schema("""{"type":"object","properties":{"recipient_id":{"type":"integer"},"recipient_name":{"type":"string"},"subject":{"type":"string"},"message":{"type":"string"}},"required":["recipient_id","recipient_name","subject","message"]}""")),
        new("post_course_update", "PROPOSE a course or group update in the currently selected Schoology space. Explicit confirmation is required.", Schema("""{"type":"object","properties":{"body":{"type":"string"}},"required":["body"]}""")),
        new("update_assignment_grade", "PROPOSE a teacher grade change. Supply target section_id, assignment_id (grade item ID), enrollment_id, grade, and optional comment. Schoology enforces teacher permissions after confirmation.", Schema("""{"type":"object","properties":{"section_id":{"type":"integer"},"assignment_id":{"type":"integer"},"enrollment_id":{"type":"integer"},"grade":{"type":"string"},"comment":{"type":"string"},"comment_status":{"type":"boolean"}},"required":["section_id","assignment_id","enrollment_id","grade"]}"""))
    ];

    internal async Task<JsonElement> ExecuteAsync(string name, JsonElement args, CancellationToken ct = default)
    {
        var sectionId = ReadLongAny(args, "course_id");
        if (sectionId <= 0 && _context.SelectedEntityKind == "Courses") sectionId = _context.SelectedEntityId;
        switch (name)
        {
            case "get_current_page":
                return Json(new { page = _context.Page, selectedEntityKind = _context.SelectedEntityKind,
                    selectedEntityId = _context.SelectedEntityId, selectedEntityName = _context.SelectedEntityName,
                    learningUserId = LearningUserId, learningUserName = _context.LearningUserName,
                    isParentLearningContext = IsParentLearningContext,
                    assignment = _context.AssignmentId > 0 ? new { sectionId = _context.AssignmentSectionId,
                        assignmentId = _context.AssignmentId, gradeItemId = _context.GradeItemId,
                        allowsSubmission = _context.AllowsSubmission } : null });

            case "get_courses":
                return Json((await _api.GetCoursesAsync(_session, LearningUserId, ct)).Select(item => new { id = item.Id, title = item.Name, section = item.Subtitle }).ToArray());

            case "get_groups":
                return Json((await _api.GetGroupsAsync(_session, LearningUserId, ct)).Select(item => new { id = item.Id, name = item.Name, description = item.Subtitle }).ToArray());

            case "get_pending_requests":
            {
                var results = await Task.WhenAll(_api.GetFriendRequestsAsync(_session, ct),
                    _api.GetGroupInvitationsAsync(_session, ct), _api.GetSectionInvitationsAsync(_session, ct));
                using var friends = results[0];
                using var groups = results[1];
                using var sections = results[2];
                object[] Map(JsonElement root, string type, params string[] keys) =>
                    SchoologyApiClient.ReadRecords(root, keys).Select(record => new
                    {
                        type,
                        id = ReadLongAny(record.Data, "id", "request_id", "requestId", "invite_id", "inviteId"),
                        name = type == "friend"
                            ? ReadString(record.Data, "requester_name", "requesterName", "name", "display_name") ?? "Friend request"
                            : ReadString(record.Data, "name", "title", type == "group" ? "group_name" : "section_title") ?? "Invitation",
                        details = ReadString(record.Data, "school_name", "schoolName", "description", "message") ?? ""
                    }).Where(item => item.id > 0).Cast<object>().ToArray();
                return Json(new { friendRequests = Map(friends.RootElement, "friend", "request", "requests"),
                    groupInvitations = Map(groups.RootElement, "group", "invite", "invites"),
                    sectionInvitations = Map(sections.RootElement, "section", "invite", "invites") });
            }

            case "respond_to_request":
            {
                var type = ReadString(args, "type") ?? "";
                var id = ReadLongAny(args, "id", "request_id", "invite_id");
                var accept = args.TryGetProperty("accept", out var acceptValue) && acceptValue.ValueKind == JsonValueKind.True;
                if (type is not ("friend" or "group" or "section") || id <= 0)
                    throw new InvalidOperationException("Choose a valid request type and an exact request ID returned by get_pending_requests.");
                using var pending = type switch
                {
                    "friend" => await _api.GetFriendRequestsAsync(_session, ct),
                    "group" => await _api.GetGroupInvitationsAsync(_session, ct),
                    _ => await _api.GetSectionInvitationsAsync(_session, ct)
                };
                var keys = type == "friend" ? new[] { "request", "requests" } : new[] { "invite", "invites" };
                var matches = SchoologyApiClient.ReadRecords(pending.RootElement, keys)
                    .Where(item => ReadLongAny(item.Data, "id", "request_id", "requestId", "invite_id", "inviteId") == id).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("That request is no longer pending or its ID is ambiguous. Refresh requests and try again.");
                var name = type == "friend"
                    ? ReadString(matches[0].Data, "requester_name", "requesterName", "name", "display_name") ?? "Friend request"
                    : ReadString(matches[0].Data, "name", "title", type == "group" ? "group_name" : "section_title") ?? "Invitation";
                return Json(new { pendingAction = new { type = "respond_to_request", requestType = type,
                    requestId = id, accept, requestName = name } });
            }

            case "get_upcoming_events":
            {
                var days = (int)Math.Clamp(ReadLong(args, "days", 30), 1, 120);
                var end = DateTime.Today.AddDays(days);
                var events = new List<JsonElement>();
                for (var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); month <= end && events.Count < 250; month = month.AddMonths(1))
                    events.AddRange((await _api.GetCalendarEventsForMonthAsync(_session, month, LearningUserId, ct)).Select(item => item.Data));
                return Json(events.Where(item =>
                {
                    var due = ReadString(item, "start", "due", "due_date", "end");
                    return DateTime.TryParse(due, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var date) &&
                        date.Date >= DateTime.Today && date.Date <= end;
                }).Take(100).ToArray());
            }

            case "search_assignments":
            {
                var query = ReadString(args, "query")?.Trim() ?? "";
                if (query.Length == 0) throw new InvalidOperationException("Enter a word or phrase to search assignments.");
                var courses = sectionId > 0
                    ? new[] { (Id: sectionId, Name: _context.SelectedEntityName) }
                    : (await _api.GetCoursesAsync(_session, LearningUserId, ct)).Take(35).Select(item => (Id: item.Id, Name: item.Name)).ToArray();
                var results = new List<object>();
                foreach (var course in courses)
                {
                    try
                    {
                        using var document = await _api.GetAssignmentsAsync(_session, course.Id, ct);
                        foreach (var record in SchoologyApiClient.ReadRecords(document.RootElement, "assignment", "assignments"))
                        {
                            var title = ReadString(record.Data, "title", "name") ?? record.Title;
                            var description = ReadString(record.Data, "description", "body") ?? "";
                            if (!(title + " " + description).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                            results.Add(new { id = ReadLongAny(record.Data, "id", "assignment_id"), title,
                                due = ReadString(record.Data, "due", "due_date", "dueDate"), description,
                                points = ReadString(record.Data, "max_points", "maxPoints"), course = course.Name, sectionId = course.Id });
                        }
                    }
                    catch (Exception ex) { results.Add(new { course = course.Name, error = ex.Message }); }
                }
                return Json(results.Take(50).ToArray());
            }

            case "get_course_grades":
            {
                if (sectionId <= 0) throw new InvalidOperationException("Open a course or supply course_id to inspect grades.");
                using var section = await _api.GetSectionDetailsAsync(_session, sectionId, ct);
                using var periods = await _api.GetGradingPeriodsAsync(_session, sectionId, ct);
                using var categories = await _api.GetGradingCategoriesAsync(_session, sectionId, ct);
                using var items = await _api.GetGradeItemsAsync(_session, sectionId, ct);
                using var grades = await _api.GetUserGradesForSectionAsync(_session, sectionId, LearningUserId, ct);
                JsonElement[] sectionGradeRecords = [];
                JsonElement[] enrollmentRecords = [];
                string? sectionGradeError = null;
                string? enrollmentError = null;
                try
                {
                    using var sectionGradesDocument = await _api.GetSectionGradesAsync(_session, sectionId, ct);
                    sectionGradeRecords = Records(sectionGradesDocument.RootElement, "grade", "grades", "section", "sections");
                }
                catch (Exception ex) { sectionGradeError = ex.Message; }
                try
                {
                    using var enrollmentDocument = await _api.GetSectionEnrollmentsAsync(_session, sectionId, ct);
                    enrollmentRecords = Records(enrollmentDocument.RootElement, "enrollment", "enrollments");
                }
                catch (Exception ex) { enrollmentError = ex.Message; }
                return Json(new { courseId = sectionId, section = section.RootElement.Clone(),
                    gradingPeriods = Records(periods.RootElement, "grading_period", "grading_periods"),
                    gradingCategories = Records(categories.RootElement, "grading_category", "grading_categories"),
                    gradeItems = Records(items.RootElement, "assignment", "assignments", "grade_item", "grade_items"),
                    userGrades = Records(grades.RootElement, "section", "sections", "grade", "grades"),
                    sectionGrades = sectionGradeRecords, sectionGradeError,
                    enrollments = enrollmentRecords, enrollmentError });
            }

            case "get_course_materials":
            {
                if (sectionId <= 0) throw new InvalidOperationException("Open a course or supply course_id.");
                using var document = await _api.GetCourseFolderAsync(_session, sectionId, Math.Max(0, ReadLongAny(args, "folder_id")), ct);
                return Json(Records(document.RootElement, "folder-item", "folder_item", "folderItems", "items"));
            }

            case "get_course_updates":
            {
                if (sectionId <= 0) throw new InvalidOperationException("Open a course or supply course_id.");
                using var document = await _api.GetEntityTabAsync(_session, "Courses", sectionId, "Updates", ct);
                return Json(Records(document.RootElement, "update", "updates", "update_list"));
            }

            case "search_message_recipients":
            {
                var query = ReadString(args, "query")?.Trim() ?? "";
                if (query.Length < 2) throw new InvalidOperationException("Enter at least two characters to search message recipients.");
                using var document = await _api.GetMessageRecipientsAsync(_session, query, ct);
                var recipients = SchoologyApiClient.ReadRecords(document.RootElement, "recipient", "recipients", "user", "users")
                    .Select(record => record.Data)
                    .Select(candidate =>
                    {
                        var user = candidate.TryGetProperty("user", out var nested) ? nested : candidate;
                        return new { id = ReadLongAny(user, "id", "uid", "user_id", "userId", "recipient_id", "recipientId"),
                            name = ReadString(user, "name_display", "nameDisplay", "display_name", "displayName", "name", "full_name", "fullName") ?? "" };
                    }).Where(item => item.id > 0 && !string.IsNullOrWhiteSpace(item.name)).DistinctBy(item => item.id).Take(20).ToArray();
                return Json(new { query, recipients });
            }

            case "search_messages":
            {
                var query = ReadString(args, "query")?.Trim() ?? "";
                if (query.Length == 0) throw new InvalidOperationException("Enter a word or phrase to search messages.");
                var folder = ReadString(args, "folder") ?? "both";
                if (folder is not ("inbox" or "sent" or "both")) folder = "both";
                var limit = (int)Math.Clamp(ReadLong(args, "limit", 30), 1, 60);
                var folders = folder == "both" ? new[] { false, true } : new[] { folder == "sent" };
                var found = new Dictionary<string, (long Updated, object Message)>(StringComparer.Ordinal);
                var folderErrors = new List<string>();
                foreach (var sent in folders)
                {
                    try
                    {
                        using var document = await _api.GetMessagesAsync(_session, sent, ct);
                        foreach (var record in SchoologyApiClient.ReadRecords(document.RootElement, "message", "messages", "items"))
                        {
                            var data = record.Data;
                            var subject = ReadString(data, "subject") ?? "";
                            var body = ReadString(data, "message", "body") ?? "";
                            var authorName = ReadString(data, "author_name", "authorName", "name_display", "nameDisplay")
                                ?? (data.TryGetProperty("author", out var authorData) ? ReadString(authorData, "name", "name_display", "nameDisplay") : null)
                                ?? "";
                            if (!(subject + " " + body + " " + authorName).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                            var id = ReadLongAny(data, "id", "message_id");
                            var key = id > 0 ? id.ToString(CultureInfo.InvariantCulture) : JsonSerializer.Serialize(data);
                            var lastUpdated = ReadLongAny(data, "last_updated", "lastUpdated", "created");
                            found.TryAdd(key, (lastUpdated, new
                            {
                                id, messageId = id.ToString(CultureInfo.InvariantCulture),
                                folder = sent ? "sent" : "inbox",
                                subject = string.IsNullOrWhiteSpace(subject) ? "(No subject)" : subject,
                                body, authorName,
                                authorId = ReadLongAny(data, "author_id", "authorId"),
                                recipientIds = ReadProperty(data, "recipient_ids", "recipientIds"),
                                lastUpdated,
                                status = ReadString(data, "message_status", "messageStatus"),
                                attachments = ReadProperty(data, "attachments", "attachment", "links")
                            }));
                        }
                    }
                    catch (Exception ex) { folderErrors.Add($"{(sent ? "sent" : "inbox")}: {ex.Message}"); }
                }
                return Json(new { messages = found.Values.OrderByDescending(item => item.Updated).Take(limit).Select(item => item.Message).ToArray(), folderErrors });
            }

            case "get_message_thread":
            {
                var id = ReadLongAny(args, "message_id");
                if (id <= 0) throw new InvalidOperationException("A valid Schoology message ID is required.");
                var folder = ReadString(args, "folder") ?? "both";
                if (folder is not ("inbox" or "sent" or "both")) folder = "both";
                var messages = new List<object>();
                var messageRecords = new List<JsonElement>();
                var folderStatuses = new List<object>();
                var folderErrors = new List<string>();
                var folders = folder == "both" ? new[] { "inbox", "sent" } : new[] { folder };
                foreach (var currentFolder in folders)
                {
                    try
                    {
                        JsonElement[] records;
                        if (currentFolder == "inbox")
                        {
                            // Android/Tauri locate inbox messages in the inbox list; the
                            // inbox detail route is not consistently available.
                            using var document = await _api.GetMessagesAsync(_session, sent: false, limit: 200, cancellationToken: ct);
                            records = Records(document.RootElement, "message", "messages", "items")
                                .Where(item => ReadLongAny(item, "id", "message_id") == id).ToArray();
                        }
                        else
                        {
                            using var document = await _api.GetMessageAsync(_session, id, sent: true, cancellationToken: ct);
                            var returned = Records(document.RootElement, "message", "messages", "items");
                            records = returned.Length > 0 ? returned : new[] { document.RootElement.Clone() };
                        }

                        folderStatuses.Add(new { folder = currentFolder, error = (string?)null });
                        foreach (var record in records)
                        {
                            messages.Add(new { folder = currentFolder, message = record });
                            messageRecords.Add(record);
                        }
                    }
                    catch (Exception ex) { folderStatuses.Add(new { folder = currentFolder, error = ex.Message }); folderErrors.Add($"{currentFolder}: {ex.Message}"); }
                }
                if (messageRecords.Count == 0)
                    throw new InvalidOperationException("Schoology returned no message details for that ID. " + string.Join(" · ", folderErrors));

                var participantIds = new HashSet<long>();
                foreach (var message in messageRecords)
                {
                    var author = ReadLongAny(message, "author_id", "authorId");
                    if (author > 0) participantIds.Add(author);
                    var rawRecipients = ReadProperty(message, "recipient_ids", "recipientIds");
                    if (rawRecipients.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in rawRecipients.EnumerateArray())
                            if (long.TryParse(value.ToString(), out var recipient) && recipient > 0) participantIds.Add(recipient);
                    }
                    else if (rawRecipients.ValueKind == JsonValueKind.String)
                    {
                        foreach (var token in rawRecipients.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (long.TryParse(token, out var recipient) && recipient > 0) participantIds.Add(recipient);
                    }
                }
                var participants = new List<object>();
                foreach (var userId in participantIds.Take(12))
                {
                    try
                    {
                        using var user = await _api.GetUserAsync(_session, userId, ct);
                        var data = user.RootElement.TryGetProperty("user", out var nested) ? nested : user.RootElement;
                        participants.Add(new { id = userId, name = ReadString(data, "name_display", "nameDisplay", "name") ?? $"User {userId}",
                            picture = ReadString(data, "picture_url", "pictureUrl") });
                    }
                    catch { participants.Add(new { id = userId, name = $"User {userId}" }); }
                }
                return Json(new { requestedMessageId = id, folders = folderStatuses, messages, participants });
            }

            case "get_current_assignment":
            {
                if (_context.AssignmentId <= 0 || _context.AssignmentSectionId <= 0)
                    throw new InvalidOperationException("Open an assignment first.");
                using var assignment = await _api.GetAssignmentAsync(_session, _context.AssignmentSectionId, _context.AssignmentId, ct);
                var data = assignment.RootElement.TryGetProperty("assignment", out var nested) ? nested.Clone() : assignment.RootElement.Clone();
                JsonElement? submission = null;
                string? submissionError = null;
                if (_context.GradeItemId > 0)
                {
                    try
                    {
                        using var document = await _api.GetAssignmentSubmissionAsync(_session, _context.AssignmentSectionId, _context.GradeItemId, LearningUserId, ct);
                        submission = document.RootElement.Clone();
                    }
                    catch (Exception ex) { submissionError = ex.Message; }
                }
                return Json(new { assignment = data, submission, submissionError,
                    submissionContext = new { sectionId = _context.AssignmentSectionId, assignmentId = _context.AssignmentId,
                        gradeItemId = _context.GradeItemId, userId = LearningUserId, learnerName = _context.LearningUserName },
                    allowsSubmission = _context.AllowsSubmission });
            }

            case "get_assignment_submission":
            {
                var requestedSection = ReadLongAny(args, "section_id");
                var submissionId = ReadLongAny(args, "submission_id");
                var userId = ReadLong(args, "user_id", LearningUserId);
                if (IsParentLearningContext && userId != LearningUserId)
                    throw new InvalidOperationException("A parent account can only inspect submissions for the student currently selected in Switch Student.");
                if (requestedSection <= 0 || submissionId <= 0 || userId <= 0)
                    throw new InvalidOperationException("Valid section_id, submission_id, and user_id are required.");
                using var document = await _api.GetAssignmentSubmissionAsync(_session, requestedSection, submissionId, userId, ct);
                return Json(new { sectionId = requestedSection, submissionId, userId, submission = document.RootElement.Clone(),
                    revisions = Records(document.RootElement, "revision", "revisions", "submission"),
                    attachments = ReadProperty(document.RootElement, "attachments", "files") });
            }

            case "submit_assignment_text":
            {
                var markdown = ReadString(args, "markdown", "text")?.Trim() ?? "";
                if (markdown.Length == 0) throw new InvalidOperationException("Provide the complete assignment response in Markdown.");
                var targetSection = ReadLong(args, "section_id", _context.AssignmentSectionId);
                var targetAssignment = ReadLong(args, "assignment_id", _context.AssignmentId);
                if (IsParentLearningContext)
                    throw new InvalidOperationException("Schoology's assignment submission endpoint does not expose a verified parent-on-behalf-of-student parameter. You can review the selected student's work, but this API integration will not submit as the parent.");
                if (!_context.AllowsSubmission || _context.AssignmentId <= 0 ||
                    targetSection != _context.AssignmentSectionId || targetAssignment != _context.AssignmentId)
                    throw new InvalidOperationException("Open the target assignment in ClassWire and make sure its dropbox accepts submissions before proposing a text submission.");
                return Json(new { pendingAction = new { type = "submit_assignment_text", markdown,
                    sectionId = targetSection, assignmentId = targetAssignment, gradeItemId = _context.GradeItemId } });
            }

            case "send_message":
            {
                var recipient = ReadString(args, "recipient_name")?.Trim() ?? "";
                var recipientId = ReadLongAny(args, "recipient_id", "recipientId");
                var subject = ReadString(args, "subject")?.Trim() ?? "";
                var message = ReadString(args, "message")?.Trim() ?? "";
                if (recipient.Length == 0 || recipientId <= 0 || subject.Length == 0 || message.Length == 0)
                    throw new InvalidOperationException("A verified recipient ID, recipient name, subject, and message are required. Search recipients first.");
                return Json(new { pendingAction = new { type = "send_message", recipientId, recipientName = recipient, subject, message } });
            }

            case "post_course_update":
            {
                var body = ReadString(args, "body")?.Trim() ?? "";
                if (body.Length == 0) throw new InvalidOperationException("The update body cannot be empty.");
                if (_context.SelectedEntityKind is not ("Courses" or "Groups") || _context.SelectedEntityId <= 0)
                    throw new InvalidOperationException("Open the course or group where you want to post the update.");
                return Json(new { pendingAction = new { type = "post_update", kind = _context.SelectedEntityKind,
                    entityId = _context.SelectedEntityId, entityName = _context.SelectedEntityName, body } });
            }

            case "update_assignment_grade":
            {
                if (IsParentLearningContext)
                    throw new InvalidOperationException("Grade changes are teacher actions and are not available in a parent learning context.");
                var targetSection = ReadLongAny(args, "section_id");
                var assignmentId = ReadLongAny(args, "assignment_id");
                var enrollmentId = ReadLongAny(args, "enrollment_id");
                var grade = ReadString(args, "grade")?.Trim() ?? "";
                if (targetSection <= 0 || assignmentId <= 0 || enrollmentId <= 0 || grade.Length == 0)
                    throw new InvalidOperationException("A valid section, grade-item ID, enrollment ID, and grade are required.");
                using var enrollmentDocument = await _api.GetSectionEnrollmentsAsync(_session, targetSection, ct);
                var enrollment = SchoologyApiClient.ReadRecords(enrollmentDocument.RootElement, "enrollment", "enrollments")
                    .FirstOrDefault(record => ReadLongAny(record.Data, "id", "enrollment_id", "enrollmentId") == enrollmentId);
                if (enrollment is null)
                    throw new InvalidOperationException("That enrollment ID was not found in the selected section; no grade proposal was created.");
                using var gradeItemDocument = await _api.GetAssignmentGradeItemAsync(_session, targetSection, assignmentId, ct);
                var gradeItemData = gradeItemDocument.RootElement.TryGetProperty("assignment", out var gradeItemNested)
                    ? gradeItemNested : gradeItemDocument.RootElement;
                var studentName = ReadString(enrollment.Data, "name_display", "nameDisplay", "student_name", "studentName", "name");
                var studentId = ReadEnrollmentUserId(enrollment.Data);
                if (string.IsNullOrWhiteSpace(studentName) && enrollment.Data.TryGetProperty("user", out var enrollmentUser))
                    studentName = ReadString(enrollmentUser, "name_display", "nameDisplay", "name");
                if (string.IsNullOrWhiteSpace(studentName) && studentId > 0)
                {
                    using var student = await _api.GetUserAsync(_session, studentId, ct);
                    var studentData = student.RootElement.TryGetProperty("user", out var studentNested) ? studentNested : student.RootElement;
                    studentName = ReadString(studentData, "name_display", "nameDisplay", "name")
                        ?? string.Join(" ", new[] { ReadString(studentData, "first_name"), ReadString(studentData, "last_name") }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
                if (string.IsNullOrWhiteSpace(studentName))
                    throw new InvalidOperationException("Schoology did not return the selected student's name. No grade-change proposal was created.");
                var assignmentTitle = ReadString(gradeItemData, "title", "name") ?? $"Grade item {assignmentId}";
                return Json(new { pendingAction = new { type = "modify_grade", sectionId = targetSection,
                    assignmentId, enrollmentId, studentId, studentName, assignmentTitle, grade,
                    comment = ReadString(args, "comment") ?? "", commentStatus = ReadBool(args, "comment_status") } });
            }
            default:
                throw new InvalidOperationException("Tool unavailable: " + name);
        }
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement[] Records(JsonElement root, params string[] keys) =>
        SchoologyApiClient.ReadRecords(root, keys).Select(record => record.Data).ToArray();

    private static JsonElement ReadProperty(JsonElement root, params string[] keys)
    {
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var key in keys) if (root.TryGetProperty(key, out var value)) return value.Clone();
        return JsonSerializer.SerializeToElement((object?)null);
    }

    private static string? ReadString(JsonElement root, params string[] keys)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
            if (root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return value.ToString();
        return null;
    }

    private static long ReadLong(JsonElement root, string key, long fallback = 0) =>
        long.TryParse(ReadString(root, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static long ReadLongAny(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
            if (long.TryParse(ReadString(root, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        return 0;
    }

    private static long ReadEnrollmentUserId(JsonElement enrollment)
    {
        var id = ReadLongAny(enrollment, "uid", "user_id", "userId", "student_id", "studentId");
        if (id <= 0 && enrollment.ValueKind == JsonValueKind.Object && enrollment.TryGetProperty("user", out var user))
            id = ReadLongAny(user, "id", "uid", "user_id", "userId");
        return id;
    }

    private static bool ReadBool(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out var parsed) && parsed);
}
