import {
    SNSClient,
    PublishCommand
} from "@aws-sdk/client-sns";

const TOPIC_ARN_NAME = "arn:aws:sns:us-east-1:339712820937:Yevhen-UploadsNotificationTopic";
const REGION = "us-east-1";

const sns = new SNSClient({
    region: REGION
});

export const lambdaHandler = async (event, context) => {
    if (!event) {
        return {
            'statusCode': 200,
            'body': JSON.stringify('No messages to process. Lambda function completed')
        };
    }

    //console.log(event.Records);

    const processed = await processRecords(event.Records);

    console.log(`SNS TOPIC ARN = ${TOPIC_ARN_NAME};
                Function Name = ${context.functionName};
                Processed Messages count = ${processed}; 
                Remaining Time in millis = ${context.getRemainingTimeInMillis()}
              `);

    return {
        'statusCode': 200,
        'body': JSON.stringify('Lambda function completed')
    };
};

const processRecords = async (records) => {
    if (!records) {
        console.log("No records to process.");
        return 0;
    }

    for (const record of records) {
        if (!record.body) {
            throw new Error('No body in SQS record.');
        }
        await sns.send(new PublishCommand({
            TopicArn: TOPIC_ARN_NAME,
            Subject: "Processed SQS Queue Messages",
            Message: record.body
        }));

        console.log(`Message ${record.body} processed successfully.`);
    }

    return records.length;
}