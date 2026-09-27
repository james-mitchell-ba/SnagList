resource "aws_ecr_repository" "api" {
  name                 = "${var.name_prefix}-api"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecr_repository" "mcp" {
  name                 = "${var.name_prefix}-mcp"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

# The GitHub OIDC provider is a once-per-AWS-account resource — assumed to already exist rather
# than created here, so a second repository in the same account doesn't collide trying to create it.
data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

resource "aws_iam_role" "github_actions_deploy" {
  name = "${var.name_prefix}-github-actions-deploy"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = data.aws_iam_openid_connect_provider.github.arn }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = { "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com" }
        StringLike   = { "token.actions.githubusercontent.com:sub" = "repo:${var.github_repository}:*" }
      }
    }]
  })
}

resource "aws_iam_role_policy" "github_actions_ecr_push" {
  name = "ecr-push"
  role = aws_iam_role.github_actions_deploy.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
      {
        Effect = "Allow"
        Action = ["ecr:BatchCheckLayerAvailability", "ecr:PutImage", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload"]
        Resource = [aws_ecr_repository.api.arn, aws_ecr_repository.mcp.arn]
      },
    ]
  })
}

resource "aws_iam_role" "lambda_execution" {
  name = "${var.name_prefix}-lambda-execution"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "lambda.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_basic" {
  role       = aws_iam_role.lambda_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "lambda_config_access" {
  name   = "config-access"
  role   = aws_iam_role.lambda_execution.name
  policy = var.lambda_config_access_policy_json
}

resource "aws_iam_role_policy" "lambda_ses_send" {
  name   = "ses-send"
  role   = aws_iam_role.lambda_execution.name
  policy = var.lambda_ses_send_policy_json
}

resource "aws_iam_role_policy" "lambda_s3_photos" {
  name = "s3-photos"
  role = aws_iam_role.lambda_execution.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
      Resource = "${var.photos_bucket_arn}/*"
    }]
  })
}

locals {
  common_environment = {
    Database__SecretArn                 = var.db_app_secret_arn
    Database__Host                      = var.db_address
    Database__Port                      = "5432"
    Database__Name                      = var.db_name
    Storage__Provider                   = "S3"
    Storage__BucketName                 = var.photos_bucket_name
    Email__Provider                     = "Ses"
    Email__FromAddress                  = var.email_from_address
    Notifications__MaintenanceTeamEmail = var.maintenance_team_email
    Auth__Provider                      = "EntraId"
    Auth__EntraId__TenantId             = var.entra_tenant_id
    Auth__EntraId__Audience             = var.entra_audience
  }
}

resource "aws_lambda_function" "api" {
  function_name = "${var.name_prefix}-api"
  role          = aws_iam_role.lambda_execution.arn
  package_type  = "Image"
  image_uri     = "${aws_ecr_repository.api.repository_url}:${var.image_tag}"
  timeout       = 30
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.lambda_security_group_id]
  }

  environment {
    variables = local.common_environment
  }
}

resource "aws_lambda_function" "mcp" {
  function_name = "${var.name_prefix}-mcp"
  role          = aws_iam_role.lambda_execution.arn
  package_type  = "Image"
  image_uri     = "${aws_ecr_repository.mcp.repository_url}:${var.image_tag}"
  timeout       = 30
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.lambda_security_group_id]
  }

  environment {
    variables = local.common_environment
  }
}

resource "aws_apigatewayv2_api" "api" {
  name          = "${var.name_prefix}-api"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "api" {
  api_id                 = aws_apigatewayv2_api.api.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.api.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "api_default" {
  api_id    = aws_apigatewayv2_api.api.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.api.id}"
}

resource "aws_apigatewayv2_stage" "api" {
  api_id      = aws_apigatewayv2_api.api.id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "api_gateway" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.api.execution_arn}/*/*"
}

resource "aws_apigatewayv2_api" "mcp" {
  name          = "${var.name_prefix}-mcp"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "mcp" {
  api_id                 = aws_apigatewayv2_api.mcp.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.mcp.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "mcp_default" {
  api_id    = aws_apigatewayv2_api.mcp.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.mcp.id}"
}

resource "aws_apigatewayv2_stage" "mcp" {
  api_id      = aws_apigatewayv2_api.mcp.id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "mcp_api_gateway" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.mcp.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.mcp.execution_arn}/*/*"
}
