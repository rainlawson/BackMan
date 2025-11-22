This project was a bit of a mess, but I eventually got it working about as well as I could have.



No need to install, as long as you have .NET 10.0. Executable path is: .\\BackMan\\BackMan.App\\bin\\Release\\net10.0-windows\\BackMan.App.exe

Almost all the majic happens in .\\BackMan\\BackMan.App\\Program.cs, .\\ProgramData\\BackMan\\tasks.json, with app settings in .\\BackMan\\BackMan.App\\BackMan.App.csproj and the build solution in .BackMan\\BackMan.slnx.



Examples and recommended tasks in .\\BackMan\\tasks.json



About:



I made this program as a third and likely final attempt to correct some issues I have with Windows. Namely, the lack of foresight for Microsoft to run DISM and SFC in the background, which I am almost certain they do not do. Initially I patched this using the TaskScheduler app, but this can be a difficult app to work with and it has many limitations, including inconsistencies in when it chooses to run tasks. This works, but later, when I decided I wanted other apps to launch on startup, such as my web browser, I found it difficult to get it to launch minimized. This is a fundamental issue and limitation with Windows, as each window is "sovereign," meaning getting other apps and windows to change it is janky at best. I did my best using complex batch files called by the TaskScheduler, but ultimately I thought maybe if I bypassed TaskScheduler altogether and made a whole program, maybe I could finally solve the problems by using something more robust than TaskScheduler tasks and batch files. 



I did not manage to fix that with this final project, but it is a functional tray application and I learned plenty during it's production. That's my cope so I don't think I just wasted a few days on this program.

