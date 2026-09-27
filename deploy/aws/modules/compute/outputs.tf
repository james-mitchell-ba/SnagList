output "api_invoke_url" {
  value = aws_apigatewayv2_stage.api.invoke_url
}
output "mcp_invoke_url" {
  value = aws_apigatewayv2_stage.mcp.invoke_url
}
output "github_actions_deploy_role_arn" {
  value = aws_iam_role.github_actions_deploy.arn
}
output "ecr_api_repository_url" {
  value = aws_ecr_repository.api.repository_url
}
output "ecr_mcp_repository_url" {
  value = aws_ecr_repository.mcp.repository_url
}
