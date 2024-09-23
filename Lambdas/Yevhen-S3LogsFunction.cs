using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;

namespace AwsEc2Subtask4.Lambdas
{
    public class Yevhen_S3LogsFunction
    {
        public void FunctionHandler(S3Event s3Event, ILambdaContext context)
        {
            foreach (var record in s3Event.Records)
            {
                var s3 = record.S3;
                context.Logger.LogLine($"Bucket: {s3.Bucket.Name}");
                context.Logger.LogLine($"Key: {s3.Object.Key}");
            }
        }
    }
}
