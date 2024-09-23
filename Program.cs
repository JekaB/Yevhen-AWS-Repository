using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using AwsEc2Subtask4;
using Microsoft.EntityFrameworkCore;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Image = AwsEc2Subtask4.Models.Image;
using Amazon.SimpleNotificationService.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

var topicArn = builder.Configuration["AWS:TopicArn"];
var s3Bucket = builder.Configuration["AWS:s3bucket"];

builder.Services.AddDbContext<ImageDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<AmazonSimpleNotificationServiceClient>();
builder.Services.AddAWSService<AmazonSQSClient>();
builder.Services.AddAWSService<IAmazonS3>();

builder.Services.AddScoped<IAmazonSimpleNotificationService, AmazonSimpleNotificationServiceClient>();

var app = builder.Build();

app.MapGet("/", async () =>
{
    using var client = new AmazonEC2Client(RegionEndpoint.USEast1);
    var response = await client.DescribeAvailabilityZonesAsync(new DescribeAvailabilityZonesRequest());
    var region = response.AvailabilityZones[0].RegionName;

    var httpClient = new HttpClient();

    var tokenRequest = new HttpRequestMessage(HttpMethod.Put, "http://169.254.169.254/latest/api/token");
    tokenRequest.Headers.Add("X-aws-ec2-metadata-token-ttl-seconds", "21600");

    var tokenResponse = await httpClient.SendAsync(tokenRequest);
    var token = await tokenResponse.Content.ReadAsStringAsync();

    httpClient.DefaultRequestHeaders.Add("X-aws-ec2-metadata-token", token);
    var azResponse = await httpClient.GetAsync("http://169.254.169.254/latest/meta-data/placement/availability-zone");
    var availabilityZone = await azResponse.Content.ReadAsStringAsync();


    return new
    {
        Region = region,
        AvailabilityZone = availabilityZone
    };
});

//app.MapGet("/image/download/{name}", async (string name, ImageDbContext context) =>
//{
//    var image = await context.Image
//                    .Where(m => m.Name == name)
//                    .FirstOrDefaultAsync();

//    if (image == null)
//    {
//        return Results.NotFound("Image not found");
//    }

//    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles", image.Name);
//    if (!File.Exists(filePath))
//    {
//        return Results.NotFound("File not found");
//    }

//    var fileStream = File.OpenRead(filePath);
//    return Results.File(fileStream, "application/octet-stream", image.Name);
//});

app.MapGet("/image/download/{name}", async (string name, IAmazonS3 s3Client) =>
{
    try
    {
        var bucketName = s3Bucket;
        var objectKey = $"{name}";

        var request = new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = objectKey
        };

        try
        {
            await s3Client.GetObjectMetadataAsync(request);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound("Image not found");
        }

        var getPreSignedUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddMinutes(5)
        };
        var downloadUrl = s3Client.GetPreSignedURL(getPreSignedUrlRequest);

        return Results.Redirect(downloadUrl);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/image/metadata/{name}", async (string name, ImageDbContext context) =>
{
    var image = await context.Image
        .Where(m => m.Name == name)
        .FirstOrDefaultAsync();

    if (image == null)
    {
        return Results.NotFound("Image not found");
    }

    return Results.Ok(new
    {
        image.Name,
        image.LastUpdateDate,
        image.ImageSize,
        image.Extension
    });
});

app.MapGet("/image/random", async (ImageDbContext context) =>
{
    int count = await context.Image.CountAsync();
    if (count == 0)
    {
        return Results.NotFound("No images available");
    }

    Random random = new();
    int index = random.Next(count);

    var image = await context.Image.OrderBy(m => m.Id).Skip(index).FirstOrDefaultAsync();

    if (image == null)
    {
        return Results.NotFound("Image not found");
    }

    return Results.Ok(new
    {
        image.Name,
        image.ImageSize,
        image.Extension,
        image.LastUpdateDate
    });
});

app.MapPost("/image/upload", async (HttpRequest request, ImageDbContext context, IAmazonS3 s3Client) =>
{
    try
    {
        var file = request.Form.Files.FirstOrDefault();

        if (file != null && file.Length > 0)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.FileName);
            var extension = Path.GetExtension(file.FileName);

            var bucketName = s3Bucket;
            var objectKey = $"{fileName}{extension}";

            using (var imageStream = file.OpenReadStream())
            {
                var s3request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    InputStream = imageStream,
                    ContentType = file.ContentType
                };
                await s3Client.PutObjectAsync(s3request);
            }

            var image = new Image
            {
                Name = fileName,
                LastUpdateDate = DateTime.Now,
                ImageSize = file.Length,
                Extension = extension
            };

            context.Image.Add(image);
            await context.SaveChangesAsync();

            var downloadUrl = $"https://{bucketName}.s3.amazonaws.com/{Uri.EscapeDataString(objectKey)}";

            return Results.Ok(downloadUrl);
        }

        return Results.BadRequest("No file provided");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

//app.MapPost("/image/upload", async (HttpRequest request, ImageDbContext context, IAmazonSimpleNotificationService snsClient) =>
//{
//    try
//    {
//        var file = request.Form.Files.FirstOrDefault();

//        if (file != null && file.Length > 0)
//        {
//            var fileName = Path.GetFileName(file.FileName);
//            var lastUpdateDate = DateTime.UtcNow;
//            var extension = Path.GetExtension(fileName);
//            var size = file.Length;

//            var image = new Image
//            {
//                Name = fileName,
//                LastUpdateDate = lastUpdateDate,
//                ImageSize = size,
//                Extension = extension
//            };

//            context.Image.Add(image);
//            await context.SaveChangesAsync();

//            var baseUrl = $"{request.Scheme}://{request.Host}";

//            string message = $"An image has been uploaded:\nName: {image.Name}\nSize: {image.ImageSize} bytes\nExtension: {image.Extension}\nDownload Link: {baseUrl}/image/download/{image.Name}";

//            var publishRequest = new Amazon.SimpleNotificationService.Model.PublishRequest
//            {
//                TopicArn = topicArn,
//                Message = message
//            };

//            await snsClient.PublishAsync(publishRequest);

//            return Results.Ok(image);
//        }

//        return Results.BadRequest("No file provided");
//    }
//    catch (Exception ex)
//    {
//        return Results.BadRequest(ex.Message);
//    }
//});

//app.MapPost("/image/upload", async (HttpRequest request, ImageDbContext context) =>
//{
//    var file = request.Form.Files.FirstOrDefault();

//    if (file != null && file.Length > 0)
//    {
//        var fileName = Path.GetFileName(file.FileName);
//        var lastUpdateDate = DateTime.UtcNow;
//        var extension = Path.GetExtension(fileName);
//        var size = file.Length;

//        var image = new Image
//        {
//            Name = fileName,
//            LastUpdateDate = lastUpdateDate,
//            ImageSize = size,
//            Extension = extension            
//        };

//        context.Image.Add(image);
//        await context.SaveChangesAsync();

//        return Results.Ok(image);
//    }

//    return Results.BadRequest("No file provided");
//});

app.MapDelete("/image/delete/{name}", async (string name, ImageDbContext context, IAmazonS3 s3Client) =>
{
    try
    {
        var image = await context.Image.FirstOrDefaultAsync(m => m.Name.Equals(name));

        if (image == null)
        {
            return Results.NotFound("Image not found");
        }

        var bucketName = s3Bucket;
        var objectKey = $"{name}";

        var request = new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = objectKey
        };

        try
        {
            await s3Client.GetObjectMetadataAsync(request);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound("Image not found");
        }

        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey
        };

        await s3Client.DeleteObjectAsync(deleteRequest);

        context.Image.Remove(image);
        await context.SaveChangesAsync();

        return Results.Ok("Image deleted successfully");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }

});

//app.MapDelete("/image/delete/{name}", async (string name, ImageDbContext context) =>
//{
//    var image = await context.Image
//        .FirstOrDefaultAsync(m => m.Name.Equals(name));

//    if (image == null)
//    {
//        return Results.NotFound("Image not found");
//    }

//    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles", image.Name);
//    if (File.Exists(filePath))
//    {
//        File.Delete(filePath);
//    }
//    else
//    {
//        return Results.NotFound("Image not found");
//    }

//    context.Image.Remove(image);
//    await context.SaveChangesAsync();

//    return Results.Ok("Image deleted successfully");
//});

app.MapPost("/notifications/subscribe", async (string email, IAmazonSimpleNotificationService snsClient) =>
{
    var request = new SubscribeRequest
    {
        Protocol = "email",
        Endpoint = email,
        TopicArn = topicArn
    };

    try
    {
        var response = await snsClient.SubscribeAsync(request);
        return Results.Ok($"Subscription successful. Subscription ARN: {response.SubscriptionArn}");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error subscribing: {ex.Message}");
    }
});

app.MapPost("/notifications/unsubscribe", async (string email, IAmazonSimpleNotificationService snsClient) =>
{
    var subscriptionArn = string.Empty;

    try
    {
        var subs = await snsClient.ListSubscriptionsByTopicAsync(new ListSubscriptionsByTopicRequest
        {
            TopicArn = topicArn
        });

        foreach (var sub in subs.Subscriptions)
        {
            if (sub.Endpoint == email)
            {
                subscriptionArn = sub.SubscriptionArn;
                break;
            }
        }

        if (string.IsNullOrEmpty(subscriptionArn))
        {
            return Results.BadRequest("No matching subscription found for the given email.");
        }

        var request = new UnsubscribeRequest
        {
            SubscriptionArn = subscriptionArn
        };

        await snsClient.UnsubscribeAsync(request);
        return Results.Ok("Unsubscribed successfully. ");
    }
    catch (AmazonSimpleNotificationServiceException awsEx)
    {
        return Results.BadRequest($"AWS Error unsubscribing: {awsEx.Message}");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error unsubscribing: {ex.Message}");
    }
});

//app.MapPost("/notifications/unsubscribe", async (string email, IAmazonSimpleNotificationService snsClient) =>
//{


//    var subscriptionArn = ????;

//    var request = new Amazon.SimpleNotificationService.Model.UnsubscribeRequest
//    {
//        SubscriptionArn = subscriptionArn
//    };

//    try
//    {
//        var response = await snsClient.UnsubscribeAsync(request);
//        return Results.Ok("Unsubscribed successfully.");
//    }
//    catch (Exception ex)
//    {
//        return Results.BadRequest($"Error unsubscribing: {ex.Message}");
//    }
//});

app.Run();