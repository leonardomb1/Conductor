<p align="center">
  <a href="" rel="noopener">
 <img width=200px height=200px src="https://github.com/user-attachments/assets/30fc446f-c032-4102-81c1-797441dfaee8" alt="Project logo"></a>
</p>

<h3 align="center">Conductor</h3>

<div align="center">

[![Status](https://img.shields.io/badge/status-active-success.svg)]()
[![GitHub Issues](https://img.shields.io/github/issues/leonardomb1/Conductor.svg)](https://github.com/leonardomb1/Conductor/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/leonardomb1/Conductor.svg)](https://github.com/leonardomb1/Conductor/pulls)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](/LICENSE)

</div>

---

<p align="center"> Conductor is a C# project designed for efficient data engineering workflows, enabling seamless orchestration between databases and RESTful JSON endpoints. This project focuses on high-performance data extraction, transformation, and loading (ETL) processes using parallelization and channel-based producer-consumer patterns. Conductor aims to streamline data extraction and management by integrating asynchronous parallel processing to handle large-scale data efficiently. 
    <br> 
</p>

## 📝 Table of Contents

- [About](#about)
- [Getting Started](#getting_started)
- [Deployment](#deployment)
- [Usage](#usage)
- [Built Using](#built_using)

## 🧐 About <a name = "about"></a>

Conductor is a powerful data extraction tool designed for high-performance ETL processes. Built using C# and optimized for parallelized workflows, it connects databases with external RESTful JSON APIs and enables efficient data manipulation. The system utilizes advanced parallelization techniques to ensure that data extraction and transformation are handled swiftly, making it ideal for handling large-scale data tasks in enterprise-level applications. Whether for synchronizing databases or fetching bulk data from APIs, Conductor simplifies the process, ensuring robust and scalable solutions.

## 🏁 Getting Started <a name = "getting_started"></a>

These instructions will get you a copy of the project up and running on your local machine for development and testing purposes. See [deployment](#deployment) for notes on how to deploy the project on a live system.

### Prerequisites

What things you need to install the software and how to install them.

    .NET SDK (>= 7.0) to run and build the project.
    To install .NET SDK, visit: https://dotnet.microsoft.com/download

### Installing

A step by step series of examples that tell you how to get a development env running.

Say what the step will be

``` bash
  git clone https://github.com/leonardomb1/Conductor.git
  cd Conductor
  dotnet publish -c Release -r {your-os-name} -o .
```

## 🎈 Usage <a name="usage"></a>

Conductor is primarily used for orchestrating high-performance ETL tasks. You can integrate it with your database systems and RESTful endpoints, allowing for scalable and parallelized data operations. The ParallelExtractionManager handles data fetching and insertion through an optimized producer-consumer pattern using channels.

## 🚀 Deployment <a name = "deployment"></a>

Deployment using executable (needs .env):

``` bash
  Conductor -h

  [Options]
    -h --help      Show this help message
    -v --version   Show version information
    -e --environment  Use environment variables for configuration
    -f --file  Use .env file for configuration
    -M --migrate Runs a migration in the configured .env database
    -eM --migrate-init-env  Runs a migration before running the server, uses the environment variables for configuration
    -fM --migrate-init-file  Runs a migration before running the server, uses the .env file for configuration
```

the following is a template for .env:

``` bash
  PORT_NUMBER=10000 
  API_FORWARDED_PORT_NUMBER=20000
  DB_FORWARED_PORT_NUMBER=32000
  CONNECTION_STRING="Data Source=app.db;Mode=ReadWriteCreate;"
  DB_TYPE="SQLite"
  SPLITTER_CHAR="|" (or any other char)
  ENABLE_LOG_DUMP=true
  LOG_DUMP_TIME_SEC=15
  ENCRYPT_KEY=some_key
  SESSION_TIME_SEC=1600
  API_KEY=some_key2
  MAX_DEGREE_PARALLEL=20
  MAX_CONSUMER_FETCH=20
  MAX_CONSUMER_ATTEMPT=10
  MAX_PRODUCER_LINECOUNT=20000
  LDAP_DOMAIN=some_domain
  LDAP_SERVER=some_server
  LDAP_PORT=636(usually for LDAPS)
  LDAP_BASEDN=seom_basedn
  LDAP_GROUPDN=some_groupdn
  LDAP_GROUPS=groups_to_which_to_filter
  LDAP_SSL=true
  LDAP_VERIFY_CERTIFICATE=false
  NODES="conductor-db" (reserved for now)
  USE_HTTPS=false
  ALLOWED_IP_ADDRESSES="127.0.0.1/32|some_other_ip" (pass any other ipaddress range, using the bit mask)
  ALLOWED_CORS="some_cors"
  QUERY_TIMEOUT_SEC=1000
  DEVELOPMENT_MODE=false
  DEBUG_DETAILED_ERROR=false
  CONNECTION_TIMEOUT_MIN=1000
  MAX_CONCURRENT_CONNECTIONS=100
  RESPONSE_CACHING_LIMIT_MB=20
  MAX_LOG_QUEUE_SIZE=10000
  REQUIRE_AUTHENTICATION=true
  POSTGRES_DB="ConductorDb"
  POSTGRES_USER="conductor"
  POSTGRES_PASSWORD="some_password"
  VERIFY_TCP=false
  VERIFY_HTTP=true
  VERIFY_CORS=false
  MAX_QUERY_PARAMS=10
  CERTIFICATE_PASSWORD="not_used"
  CERTIFICATE_PATH="not_used"
  ENCRYPT_INDICATOR_BEGIN="$$>" (used for separation in encrypted inline header values for http extraction)
  ENCRYPT_INDICATOR_END="<$$" 
  VIRTUAL_TABLE_ID_MAX_LENGHT=5
  DOCKER_ENVIRONMENT="dev" (or main)
```

You can also deploy using docker and docker compose:

``` bash
  docker compose up
```

## ⛏️ Built Using <a name = "built_using"></a>

- [Channel-based Parallelization](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) - Used Algorithm
- [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview?view=aspnetcore-9.0) -  Framework for building the REST API.
- [Linq2db](https://linq2db.github.io/) - LINQ to DB for interacting with relational databases (metadata).

