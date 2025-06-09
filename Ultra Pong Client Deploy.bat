SET "GODOT_PATH=C://Users/fefeg/Documents/Godot"

::Export Ultra Pong Linux project
"%GODOT_PATH%/Engine/4.3/Godot_v4.3-stable_mono_win64" --headless --path "%GODOT_PATH%/Projects/Ultra Pong" --export-debug "Linux/X11" "%GODOT_PATH%/Exports/Ultra Pong Linux/Ultra Pong.x86_64" 

::Clean previous version on server
ssh ultra-pong-client "rm -r ultra-pong"

::Send new version to server
scp -r "%GODOT_PATH%/exports/Ultra Pong Linux" ultra-pong-client:/home/Felipe-Cruz/ultra-pong

::Clean, build and run docker with new game version
ssh ultra-pong-client "sudo docker rm -f ultra-pong-client; sudo docker build -t ultra-pong-client .; sudo docker run -t -p 7000:7000 --name ultra-pong-client ultra-pong-client"