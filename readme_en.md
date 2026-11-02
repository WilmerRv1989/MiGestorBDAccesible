# MiGestorBDAccesible

*A screen reader accessible database manager for SQL Server, Oracle and PostgreSQL.*

Hi, I'm Wilmer. I work and study with databases, and I use a screen reader. The usual tools, like SSMS, SQL Developer or pgAdmin, are very complete, but they are hard to use with NVDA: trees that are not announced well, results you cannot move through, forms that do not tell you what they expect. I got tired of fighting with them, so I decided to make my own.

Mi Gestor BD Accesible is designed to be used entirely with the keyboard and a screen reader. It does the same job as those programs: connect to a server, explore its databases, write and run queries, and create or modify tables. But it tells you everything it does, and almost everything that changes the database shows you the SQL first and asks for confirmation.

The program and its manual are in Spanish.

* **Download:** [latest release](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest), with the installer and the user manual.
* **Source code:** this same repository, under the GPL. Further down I tell you how it is made.
* Versión en español: [readme.md](readme.md).

## What you will find

* **Three engines:** SQL Server 2017 and later, Oracle 19c and later (including Oracle Free) and PostgreSQL 14 and later.
* **An object explorer** shaped as a tree: databases, tables, columns, keys, indexes, views, procedures, functions, sequences, triggers and, in Oracle, packages. Enter describes the item in front of you, in words.
* **A query editor** with several tabs, results as tables you move through with your screen reader's table commands, messages with "Go to line" for errors, history, and find and replace.
* **Forms to create and modify tables,** indexes, CHECK constraints and foreign keys, which check your data before applying a change and warn you about what could break.
* **Edit rows** in a panel with its own transaction: you commit or roll back whenever you want, in all three engines.
* **To understand a database:** describe the whole model, with the load order and possible problems; the JOIN path between two tables; find any object by name; and see what each object uses and what uses it.
* **Help with F1** on the SQL word under the cursor, with examples for your engine, and a complete **user manual**.
* **A menu bar** with Alt or F10, to discover everything the program does and its shortcuts.

It runs on 64-bit Windows 10 and 11. I test it every day with NVDA. It does not need the Internet and does not send any data: your connections and their passwords stay on your computer, encrypted with your Windows account.

## How to install it

1. Download the installer from the [latest release](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest).
2. Since it is not digitally signed yet, Windows may show "Windows protected your PC". Choose "More info" and then "Run anyway".
3. Follow the wizard. It installs for your user only and does not ask for administrator permissions.

You will need your own database server: on your computer, on your network or in a Docker container. The program does not include one.

Next to the installer you will find the user manual as a web page: open it in your browser, without installing anything. It is also inside the program, in the Help menu.

## How I made it

Mi Gestor BD Accesible was born from a real need. Because graphical database managers are not accessible, for a long time the terminal was my only way to work with databases. It works, but I wanted more: a program made to measure, covering what I really need to study and work, that I could use from start to finish with my screen reader.

Before writing a single line, I studied the existing programs in depth. With [REA](https://github.com/morluto/rea), a reverse engineering tool that works with artificial intelligence agents, I analyzed SSMS, the SQL Server manager and the one that handles many of these tasks best, and also SQL Developer and pgAdmin. I looked at how they organize the object explorer, what each menu offers, how they create and modify tables, how they edit rows and how they show results. From there I took their main features, studied them one by one and redesigned them into a single engine-agnostic program: the same actions, with the same names and the same keys, whether you are on SQL Server, Oracle or PostgreSQL, and designed from the start for the keyboard and the screen reader.

Reverse engineering helped me understand how those programs work and what they do, not copy them: the code of Mi Gestor BD Accesible is my own and was written from scratch.

I did both that analysis and the development with the assistance of Claude, an artificial intelligence model by Anthropic. Claude ran REA, wrote most of the code following my directions, looked up each engine's documentation and suggested technical solutions. The direction and the decisions were mine: what to study, what the program does, how it behaves with the screen reader, how each thing is announced, which keys it uses and what to leave out.

I tested and approved every feature, key and message myself with NVDA. I am telling you because I think it is the honest thing to do. If you find a bug, it is my responsibility, and you would help me a lot by letting me know.

## The code, open

I want this project to be transparent from end to end, so the complete code is here, in this same repository. You can review it, see how it works inside, check that it does not send your data anywhere, or learn from it. If you only want to use the program, you do not need any of this: download the installer from the [latest release](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest).

It is written in C# with .NET 10 and Blazor inside a Windows window (WPF). To build it you need Windows and the .NET 10 SDK:

* `dotnet build` builds the solution, and `dotnet test` runs the automated tests.
* `crear-instalador.ps1` creates the installer and the manual in the `salida` folder. It needs NSIS, which you can install with `winget install NSIS.NSIS`.

Each version arrives in this repository as a single commit, with its tag and its download.

## What's next

The next version will add MySQL, MariaDB and SQLite. If you use another engine or something is missing, write to me.

## Write to me

If something does not work, if you have an idea or you just want to tell me how it went, write to me at contacto@wilmer-rv.com or open an [issue on GitHub](https://github.com/WilmerRv1989/MiGestorBDAccesible/issues). To help me help you, open the About window (Help menu), use "Copiar la información de la versión" (copy version information) and paste it into your message. I read everything.

Made with love by Wilmer Rodríguez Vega ([wilmer-rv.com](https://wilmer-rv.com)).

Licensed under the GNU GPL, version 3 or later, with an [additional permission](EXCEPCION-ORACLE.md) to be used with the official Oracle driver.

Nosce te ipsum.
