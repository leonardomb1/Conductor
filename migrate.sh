#!/bin/bash
cd ./src/Backend
read -p "Enter the migration name: " migration_name
dotnet ef migrations add "$migration_name" --msbuildprojectextensionspath ../build/Conductor/obj/ -- ../.env
