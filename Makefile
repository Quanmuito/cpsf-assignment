MAKEFLAGS += --no-print-directory --always-make
REGION=us-east-1
LOCALSTACK=http://localhost:4566
S3_BUCKET=storage
DYNAMODB_TABLE=Files

up:
	docker compose up -d

down:
	docker compose down

logs:
	docker compose logs

reset:
	docker compose down
	docker compose build
	make up

check:
	@echo "*** You might see your own AWS resources below, if you have localstack installed before ***"
	@echo "DynamoDB Table:\n$$(aws --endpoint-url=${LOCALSTACK} --region ${REGION} dynamodb list-tables --query "TableNames[]" --output json | jq -r '.[]')"
	@echo "S3 Bucket:\n$$(aws --endpoint-url=${LOCALSTACK} --region ${REGION} s3api list-buckets --query "Buckets[].Name" --output json | jq -r '.[]')"
	@echo "FileStorage API:\n$$(curl -s http://localhost:8080/health)"
	@echo "*******************************************************************************************"

storage:
	@echo "S3 Files: $$(aws --endpoint-url=${LOCALSTACK} --region ${REGION} s3api list-objects-v2 --bucket ${S3_BUCKET} --query 'Contents[].Key' | jq -r 'select(. != null) | join(", ")')\n"
	@echo "DynamoDB items:\n$$(aws --endpoint-url=${LOCALSTACK} --region ${REGION} dynamodb scan --table-name ${DYNAMODB_TABLE} --query 'Items' | jq -r '.[]')"