#!/bin/sh
set -e

# Create DynamoDB table for file's metadata
aws --endpoint-url=http://localhost:4566 --region us-east-1 dynamodb create-table \
    --table-name Files \
    --attribute-definitions AttributeName=Filename,AttributeType=S AttributeName=UploadedAt,AttributeType=S \
    --key-schema AttributeName=Filename,KeyType=HASH AttributeName=UploadedAt,KeyType=RANGE \
    --provisioned-throughput ReadCapacityUnits=5,WriteCapacityUnits=5

# Create S3 bucket
aws --endpoint-url=http://localhost:4566 --region us-east-1 s3 mb s3://storage