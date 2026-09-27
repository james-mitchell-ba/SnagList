terraform {
  required_providers {
    postgresql = { source = "cyrilgdn/postgresql", version = "~> 1.21" }
  }
}

resource "aws_db_subnet_group" "main" {
  name       = "${var.name_prefix}-db-subnets"
  subnet_ids = var.private_subnet_ids
}

resource "random_password" "master" {
  length  = 32
  special = false
}

resource "aws_db_instance" "main" {
  identifier              = "${var.name_prefix}-postgres"
  engine                  = "postgres"
  engine_version          = "16"
  instance_class          = var.instance_class
  allocated_storage       = 20
  storage_encrypted       = true
  db_name                 = "snaglist"
  username                = "snaglist_master"
  password                = random_password.master.result
  db_subnet_group_name    = aws_db_subnet_group.main.name
  vpc_security_group_ids  = [var.database_security_group_id]
  deletion_protection     = var.deletion_protection
  backup_retention_period = 7
  skip_final_snapshot     = !var.deletion_protection
}

resource "aws_secretsmanager_secret" "db_master" {
  name = "${var.name_prefix}/db/master"
}

resource "aws_secretsmanager_secret_version" "db_master" {
  secret_id     = aws_secretsmanager_secret.db_master.id
  secret_string = jsonencode({ username = aws_db_instance.main.username, password = random_password.master.result })
}

resource "random_password" "app" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "db_app" {
  name = "${var.name_prefix}/db/app"
}

resource "aws_secretsmanager_secret_version" "db_app" {
  secret_id     = aws_secretsmanager_secret.db_app.id
  secret_string = jsonencode({ username = "snaglist_app", password = random_password.app.result })
}

provider "postgresql" {
  host      = aws_db_instance.main.address
  port      = 5432
  username  = aws_db_instance.main.username
  password  = random_password.master.result
  sslmode   = "require"
  superuser = false
}

# The application role: no table-creation/schema-ownership rights, no ability to alter the schema
# EF Core migrations own — DML only. The migration runner (SnagList.SeedData, running with the
# master credentials in Task 7) is the only thing that ever runs migrations.
resource "postgresql_role" "app" {
  name     = "snaglist_app"
  login    = true
  password = random_password.app.result
}

resource "postgresql_grant" "app_schema_usage" {
  database    = aws_db_instance.main.db_name
  role        = postgresql_role.app.name
  schema      = "public"
  object_type = "schema"
  privileges  = ["USAGE"]
}

resource "postgresql_grant" "app_table_dml" {
  database    = aws_db_instance.main.db_name
  role        = postgresql_role.app.name
  schema      = "public"
  object_type = "table"
  privileges  = ["SELECT", "INSERT", "UPDATE", "DELETE"]
}
