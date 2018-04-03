# InstaCrawler
.NET Core 2.0 IG email scraper using ElasticSearch. Runs on 4 threads and munches bandwidth like no other. 

By default it will index emails and scraped users into the "emailfrominstagram" localhost ES index under /emailitem and /targetuser respectively, /targetuser will grow very big very quickly. Uses about 1 - 1.4mbps. 

## Setup:
You need ElasticSearch installed and running (V1 release is for 5.6.7, old indexes structure).

Optionally edit your environment variables and build the app for your environment.
e.g. for win10 64:

    dotnet publish -c Release -r win10-x64

## KNOWN BUGS: 
- Sometimes it double hits the same target(s) for a while.

Do with it what you must.
