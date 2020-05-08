# gh-stats

This project attempts to provide reviewer stats for a given github repository's recently completed pull requests.

## Usage

    ghstats [options] organization/repository

    Options:
     -h, --help                 Show help.
     --pages=<count>            Fetch at most this many pages of pull requests
                                (default=1).
     --state=(all|closed|open)  Count pull requests in this state (default=closed).
     --version                  Show version.
