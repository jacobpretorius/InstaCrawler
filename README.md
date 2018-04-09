# InstaCrawler
.NET Core 2.0 IG email scraper using ElasticSearch. Runs on 4 threads and munches bandwidth like no other. 

By default it will index emails and scraped emails into the "emailfrominstagram" localhost ES index and scraped accounts to "emailfrominstagramtargets", /emailfrominstagramtargets will grow very big very quickly. Uses about 1-1.4mbps. 

## Setup:
You need ElasticSearch installed and running (ES >6, I'm using 6.2.1).

Optionally edit your environment variables and build the app for your environment.
e.g. for win10 64:

    dotnet publish -c Release -r win10-x64

## KNOWN BUGS: 
- It wants to go reaaaaly fast (you should see it with 100 threads on a 8 core xeon box), but hits ratelimits in seconds.
- It used to double-hit sometimes, not sure if it still does after the refactoring.

Do with it what you must.
