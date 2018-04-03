# InstaCrawler
.NET Core 2.0 IG email scraper using ElasticSearch. Runs on 4 threads and munches bandwidth like no other. 

By default it will index emails and scraped emails into the "emailfrominstagram" localhost ES index and scraped accounts to "emailfrominstagramtargets", /emailfrominstagramtargets will grow very big very quickly. Uses about 1-1.4mbps. 

## Setup:
You need ElasticSearch installed and running (ES >6, I'm using 6.2.1).

Optionally edit your environment variables and build the app for your environment.
e.g. for win10 64:

    dotnet publish -c Release -r win10-x64

## KNOWN BUGS: 
- Sometimes it double hits the same target(s) for a while.

Do with it what you must.
