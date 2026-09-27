output "address" {
  value = aws_db_instance.main.address
}

output "db_name" {
  value = aws_db_instance.main.db_name
}

output "master_secret_arn" {
  value = aws_secretsmanager_secret.db_master.arn
}

output "app_secret_arn" {
  value = aws_secretsmanager_secret.db_app.arn
}
