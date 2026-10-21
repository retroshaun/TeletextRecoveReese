using Avalonia.Media.Imaging;

namespace TeletextRecoveReese;

public partial class MainWindow
{
    private sealed record SessionPagePreview(
        string SourcePath, int Magazine, int Page, int Subpage, int? Version);

    private string SessionPreviewPath(bool broadcast) => Path.Combine(
        Path.GetDirectoryName(_sessionStatePath)!, broadcast ? "session-full.png" : "session-squashed.png");

    private void SaveSessionPagePreviews()
    {
        SessionPagePreview? Save(bool broadcast)
        {
            var grid = broadcast ? BroadcastGrid : SquashGrid;
            string? sourcePath = broadcast ? _broadcastFilePath : _squashFilePath;
            string? savedPath = broadcast ? _sessionState.BroadcastFilePath : _sessionState.SquashFilePath;
            if (grid.Page is not { } page || string.IsNullOrWhiteSpace(sourcePath)
                || !string.Equals(sourcePath, savedPath, StringComparison.Ordinal)) return null;
            string destination = SessionPreviewPath(broadcast);
            string temporary = destination + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var output = File.Create(temporary))
                    grid.SaveScreenshotPng(output);
                File.Move(temporary, destination, overwrite: true);
                return new SessionPagePreview(sourcePath, page.Magazine, page.PageNumber,
                    page.SubPage, broadcast ? Math.Max(VersionComboBox.SelectedIndex, 0) : null);
            }
            catch { return null; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
        _sessionState.SquashPreview = Save(false);
        _sessionState.BroadcastPreview = Save(true);
    }

    private Bitmap? LoadSessionPagePreview(
        bool broadcast, string path, int? magazine, int? page, int? subpage, int? version)
    {
        SessionPagePreview? preview = broadcast ? _sessionState.BroadcastPreview : _sessionState.SquashPreview;
        if (preview is null || !string.Equals(preview.SourcePath, path, StringComparison.Ordinal)
            || preview.Magazine != magazine || preview.Page != page || preview.Subpage != subpage
            || preview.Version != version) return null;
        try
        {
            using var input = File.OpenRead(SessionPreviewPath(broadcast));
            return new Bitmap(input);
        }
        catch { return null; }
    }
}
