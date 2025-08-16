SET "GODOT_PATH=C:\Users\fefeg\Documents\Godot"

:: Export game from Godot
"%GODOT_PATH%\Engine\4.3\Godot_v4.3-stable_mono_win64.exe" --headless --path "%GODOT_PATH%\Projects\Ultra Pong" --export-debug "Linux/X11" "%GODOT_PATH%\Exports\Ultra Pong Linux\Ultra Pong.x86_64"

:: Clean up old deployment
ssh ultra-pong-client "rm -rf /home/Felipe-Cruz/ultra-pong && mkdir -p /home/Felipe-Cruz/ultra-pong"

:: Deploy via rsync
wsl rsync -avzP "/mnt/c/Users/fefeg/Documents/Godot/Exports/Ultra Pong Linux/" ultra-pong-client:/home/Felipe-Cruz/ultra-pong/

:: Stop and remove old Docker container
ssh ultra-pong-client "sudo docker stop ultra-pong-client-container 2>/dev/null || true && sudo docker rm ultra-pong-client-container 2>/dev/null || true"

:: Build new Docker image
ssh ultra-pong-client "cd /home/Felipe-Cruz && sudo docker build -t ultra-pong-client ."

pause
