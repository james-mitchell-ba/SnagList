# Copy to a real environment file (e.g. production.tfvars), fill in, never commit the
# filled-in copy — *.tfvars is gitignored, except files named exactly example.tfvars.
aws_region             = "eu-west-1"
name_prefix            = "snaglist"
availability_zones     = ["eu-west-1a", "eu-west-1b"]
deletion_protection    = true
github_repository      = "james-mitchell-ba/SnagList"
image_tag              = "REPLACE_WITH_A_PROMOTED_IMAGE_TAG"
email_domain_name      = "snaglist.example.com"
email_from_address     = "snaglist@snaglist.example.com"
maintenance_team_email = "maintenance@snaglist.example.com"
entra_tenant_id        = "REPLACE_WITH_YOUR_ENTRA_TENANT_ID"
