namespace EduForge.Application;

public record ImageAnnotation(int Number, string Kind, string Color, string Label);
public record ImageCaptionRequest(string ImageUrl, string Context, List<ImageAnnotation> Annotations);
public interface IImageCaptionAiService
{
    Task<string> GenerateCaptionAsync(byte[] image, string contentType, ImageCaptionRequest input, CancellationToken ct);
}
