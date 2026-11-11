using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Schoology.WinUI;

public sealed partial class MainWindow
{
    private async Task<bool> IsTeacherInSectionAsync(SchoologyAuthService.Session session, long sectionId)
    {
        try
        {
            using var doc = await _api.GetSectionEnrollmentsAsync(session, sectionId);
            return SchoologyApiClient.ReadRecords(doc.RootElement, "enrollment", "enrollments").Any(item =>
                JsonLong(item.Data, "uid", "user_id", "userId") == session.UserId &&
                JsonBoolean(item.Data, "admin", "is_admin", "isAdmin") &&
                JsonLong(item.Data, "status", "enrollment_status", "enrollmentStatus") is 0 or 1);
        }
        catch (Exception ex) { App.WriteStartupLog($"Could not verify teacher access to section {sectionId}.", ex); return false; }
    }

    private async Task<IReadOnlyList<SchoologyApiRecord>> LoadTeacherPagesAsync(
        Func<int,int,Task<JsonDocument>> request, params string[] keys)
    {
        const int pageSize = 200;
        var result = new List<SchoologyApiRecord>();
        for (var offset=0; offset<10000; offset+=pageSize)
        {
            using var doc = await request(offset,pageSize);
            var page = SchoologyApiClient.ReadRecords(doc.RootElement, keys);
            if (page.Count==0) break;
            result.AddRange(page);
            if (page.Count<pageSize) break;
        }
        return result;
    }

    private async Task<UIElement> BuildTeacherGradingViewAsync(
        SchoologyAuthService.Session session,long sectionId,long gradeItemId,JsonElement assignment,JsonElement gradeItem,SchoologyApiRecord record)
    {
        var panel = new StackPanel { Spacing=10, Margin=new Thickness(10) };
        var title = Text("Student submissions",18,true);
        panel.Children.Add(title);
        var roster = new ListView { SelectionMode=ListViewSelectionMode.None, IsItemClickEnabled=false,
            HorizontalContentAlignment=HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(roster,"TeacherAssignmentSubmissionRoster");
        panel.Children.Add(roster);
        var enrollTask=LoadTeacherPagesAsync((s,l)=>_api.GetSectionEnrollmentsPageAsync(session,sectionId,s,l),"enrollment","enrollments");
        var gradeTask=LoadTeacherPagesAsync((s,l)=>_api.GetSectionGradesPageAsync(session,sectionId,gradeItemId,s,l),"grade","grades");
        var subTask=LoadTeacherPagesAsync((s,l)=>_api.GetAssignmentSubmissionsPageAsync(session,sectionId,gradeItemId,s,l),"revision","revisions","submission","submissions");
        await Task.WhenAll(enrollTask,gradeTask,subTask);
        var enrollments=await enrollTask; var grades=await gradeTask; var submissions=await subTask;
        var gradeMap=grades.ToDictionary(x=>JsonLong(x.Data,"enrollment_id","enrollmentId"),x=>x.Data);
        var submissionMap=submissions.GroupBy(x=>JsonLong(x.Data,"uid","user_id","userId")).Where(g=>g.Key>0)
            .ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>JsonLong(x.Data,"created","created_at","revision_id","revisionId")).First().Data);
        var students=enrollments.Where(x=>!JsonBoolean(x.Data,"admin","is_admin","isAdmin") &&
            JsonLong(x.Data,"status","enrollment_status","enrollmentStatus") is 0 or 1).ToArray();
        foreach(var e in students)
        {
            var enrollmentId=JsonLong(e.Data,"id","enrollment_id","enrollmentId");
            var uid=JsonLong(e.Data,"uid","user_id","userId");
            if(uid<=0||enrollmentId<=0)continue;
            var name=ProfileValue(e.Data,"name_display","nameDisplay","student_name","studentName","name");
            if(string.IsNullOrWhiteSpace(name)&&e.Data.TryGetProperty("user",out var user))name=ProfileValue(user,"name_display","nameDisplay","name");
            name??="Student "+uid.ToString(CultureInfo.InvariantCulture);
            gradeMap.TryGetValue(enrollmentId,out var grade); submissionMap.TryGetValue(uid,out var submission);
            var score=grade.ValueKind==JsonValueKind.Undefined?"":ProfileValue(grade,"grade","calculated_grade")??"";
            var state=submission.ValueKind==JsonValueKind.Undefined?"Not submitted":
                JsonBoolean(submission,"draft","is_draft")?"Draft":JsonBoolean(submission,"late","is_late")?"Late":"Submitted";
            var row=new Grid{ColumnSpacing=12,Padding=new Thickness(10)};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var labels=new StackPanel{Spacing=3};labels.Children.Add(Text(name,14,true));labels.Children.Add(Text(state,12));row.Children.Add(labels);
            var scoreText=Text(string.IsNullOrWhiteSpace(score)?"—":score,13,true);Grid.SetColumn(scoreText,1);row.Children.Add(scoreText);
            var item=new ListViewItem{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            item.Tapped+=async(_,_)=>await ShowTeacherStudentSubmissionAsync(session,sectionId,gradeItemId,uid,enrollmentId,name,assignment,gradeItem,record);
            roster.Items.Add(item);
        }
        if(students.Length==0)roster.Items.Add(new ListViewItem{Content=EmptyState("No active student enrollments were returned.")});
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private async Task ShowTeacherStudentSubmissionAsync(
        SchoologyAuthService.Session session,long sectionId,long gradeItemId,long uid,long enrollmentId,string name,
        JsonElement assignment,JsonElement gradeItem,SchoologyApiRecord record)
    {
        try
        {
            using var gradeDoc=await _api.GetAssignmentGradesAsync(session,sectionId,gradeItemId);
            var grade=SchoologyApiClient.ReadRecords(gradeDoc.RootElement,"grade","grades")
                .Select(x=>x.Data).FirstOrDefault(x=>JsonLong(x,"enrollment_id","enrollmentId")==enrollmentId);
            using var subDoc=await _api.GetAssignmentSubmissionAsync(session,sectionId,gradeItemId,uid);
            var revisions=SchoologyApiClient.ReadRecords(subDoc.RootElement,"revision","revisions","submission","submissions")
                .OrderByDescending(x=>JsonLong(x.Data,"revision_id","revisionId","id")).ToArray();
            var rubricId=JsonLong(gradeItem,"grading_scale","gradingScale","rubric_id","rubricId");
            JsonElement rubric=default,rubricGrade=default; string? rubricError=null;
            if(rubricId>0)
            {
                try
                {
                    using var rubricDoc=await _api.GetGradingRubricAsync(session,sectionId,rubricId);
                    rubric=rubricDoc.RootElement.TryGetProperty("rubric",out var r)?r.Clone():rubricDoc.RootElement.Clone();
                    try{using var rg=await _api.GetRubricGradeAsync(session,rubricId,gradeItemId,enrollmentId);rubricGrade=rg.RootElement.TryGetProperty("rubric_grade",out var g)?g.Clone():rg.RootElement.Clone();}
                    catch(Exception ex){App.WriteStartupLog("No existing rubric grade found.",ex);}
                }
                catch(Exception ex){rubricError=ex.Message;}
            }
            var content=new StackPanel{Spacing=10,MinWidth=500,MaxWidth=740,Padding=new Thickness(4)};
            content.Children.Add(Text(name,19,true));
            var revisionsPanel=new StackPanel{Spacing=8};revisionsPanel.Children.Add(Text("Submission history",15,true));
            if(revisions.Length==0)revisionsPanel.Children.Add(EmptyState("This student has not submitted this assignment."));
            foreach(var item in revisions)
            {
                var data=item.Data;var block=new StackPanel{Spacing=5,Padding=new Thickness(0,8,0,10)};
                block.Children.Add(Text("Revision "+JsonLong(data,"revision_id","revisionId","id")+" · "+
                    (JsonBoolean(data,"draft","is_draft")?"Draft":JsonBoolean(data,"late","is_late")?"Late":"Submitted"),13,true));
                var body=ProfileValue(data,"body","text","comment","description");if(!string.IsNullOrWhiteSpace(body))block.Children.Add(CreateRichContent(body,13));
                var attachments=BuildAttachmentControls(data,()=>_ = ShowTeacherStudentSubmissionAsync(session,sectionId,gradeItemId,uid,enrollmentId,name,assignment,gradeItem,record));
                if(attachments.Children.Count>0)block.Children.Add(attachments);
                revisionsPanel.Children.Add(Surface(block));
            }
            content.Children.Add(Surface(revisionsPanel));
            var criterionInputs=new Dictionary<long,(TextBox Score,TextBox Comment)>();
            TextBox? numeric=null,overrideBox=null;
            if(rubricId>0&&rubricError is null&&rubric.ValueKind==JsonValueKind.Object)
            {
                content.Children.Add(Text(ProfileValue(rubric,"title","name")??"Rubric",16,true));
                var criteria=SchoologyApiClient.ReadRecords(rubric,"criteria","criterion");
                var oldCriteria=SchoologyApiClient.ReadRecords(rubricGrade,"criteria_grades","criteria_grade","criteriaGrades")
                    .ToDictionary(x=>JsonLong(x.Data,"criteria_id","criteriaId"),x=>x.Data);
                foreach(var c in criteria)
                {
                    var d=c.Data;var cid=JsonLong(d,"id","criteria_id","criteriaId");if(cid<=0)continue;
                    oldCriteria.TryGetValue(cid,out var old);
                    var card=new StackPanel{Spacing=5,Padding=new Thickness(10)};
                    card.Children.Add(Text(ProfileValue(d,"title","name")??"Criterion",14,true));
                    var desc=ProfileValue(d,"description");if(!string.IsNullOrWhiteSpace(desc))card.Children.Add(Text(desc,12));
                    var score=new TextBox{Header="Points (max "+(ProfileValue(d,"max_points","maxPoints")??"—")+")",Text=ProfileValue(old,"grade","points")??""};
                    var note=new TextBox{Header="Criterion comment",Text=ProfileValue(old,"comment")??"",AcceptsReturn=true,MinHeight=54,TextWrapping=TextWrapping.Wrap};
                    var ratings=SchoologyApiClient.ReadRecords(d,"ratings","rating");
                    if(ratings.Count>0)
                    {
                        var picker=new ComboBox{Header="Suggested rating",PlaceholderText="Choose a rating"};
                        foreach(var r in ratings){var p=ProfileValue(r.Data,"points","score")??"";picker.Items.Add(new ComboBoxItem{Content=p+" · "+(ProfileValue(r.Data,"description","title")??"Rating"),Tag=p});}
                        picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is ComboBoxItem selected&&selected.Tag is string p)score.Text=p;};
                        card.Children.Add(picker);
                    }
                    card.Children.Add(score);card.Children.Add(note);criterionInputs[cid]=(score,note);content.Children.Add(Surface(card));
                }
                overrideBox=new TextBox{Header="Override total grade (optional)",Text=ProfileValue(rubricGrade,"total_grade_override","totalGradeOverride")??""};
                content.Children.Add(overrideBox);
            }
            else
            {
                if(rubricError is not null)content.Children.Add(EmptyState("Rubric unavailable",rubricError));
                numeric=new TextBox{Header="Score",Text=ProfileValue(grade,"grade","calculated_grade")??""};content.Children.Add(numeric);
            }
            var comment=new TextBox{Header="Overall comment",Text=ProfileValue(rubricGrade,"comment")??ProfileValue(grade,"comment")??"",AcceptsReturn=true,MinHeight=80,TextWrapping=TextWrapping.Wrap};
            var display=new CheckBox{Content="Display comment to student",IsChecked=JsonBoolean(rubricGrade,"comment_status")||JsonBoolean(grade,"comment_status")};
            content.Children.Add(comment);content.Children.Add(display);
            var dialog=new ContentDialog{Title="Grade "+name,Content=new ScrollViewer{Content=content,MaxHeight=620,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},
                PrimaryButtonText="Save grade",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary,XamlRoot=_root.XamlRoot,
                RequestedTheme=_highContrastEnabled?ElementTheme.Default:_root.ActualTheme};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var visible=display.IsChecked==true;var overall=comment.Text.Trim();
            if(rubricId>0&&rubricError is null&&rubric.ValueKind==JsonValueKind.Object)
            {
                var criteriaGrades=new List<object>();
                foreach(var pair in criterionInputs)
                {
                    double? value=null;
                    if(!string.IsNullOrWhiteSpace(pair.Value.Score.Text))
                    {if(!double.TryParse(pair.Value.Score.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed))throw new InvalidOperationException("Rubric scores must be numeric.");value=parsed;}
                    criteriaGrades.Add(new{criteria_id=pair.Key,grade=value,comment=pair.Value.Comment.Text.Trim()});
                }
                var payload=new Dictionary<string,object?>{{"criteria_grades",criteriaGrades},{"comment",overall},{"comment_status",visible?1:0}};
                if(!string.IsNullOrWhiteSpace(overrideBox?.Text))
                {if(!double.TryParse(overrideBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var ov))throw new InvalidOperationException("Override grade must be numeric.");payload["total_grade_override"]=ov;}
                else payload["clear_total_grade_override"]=1;
                using var saved=await _api.UpdateAssignmentRubricGradeAsync(session,rubricId,gradeItemId,enrollmentId,payload);
            }
            else
            {
                var value=numeric?.Text.Trim()??"";if(value.Length==0)throw new InvalidOperationException("Enter a grade.");
                using var saved=await _api.UpdateAssignmentGradeAsync(session,sectionId,gradeItemId,enrollmentId,value,overall,visible);
            }
            await ShowAssignmentDetailsAsync(sectionId,record);
        }
        catch(Exception ex){await ShowApiErrorAsync("Unable to load or save teacher grading",ex.Message);}
    }
}
