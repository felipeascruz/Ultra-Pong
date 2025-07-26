SET "GODOT_PATH=C://Users/fefeg/Documents/Godot"
:: Export Ultra Pong Linux project
"%GODOT_PATH%/Engine/4.3/Godot_v4.3-stable_mono_win64" --headless --path "%GODOT_PATH%/Projects/Ultra Pong" --export-debug "Linux/X11" "%GODOT_PATH%/Exports/Ultra Pong Linux/Ultra Pong.x86_64"
:: Clean previous version on server
ssh ultra-pong-client "rm -rf ultra-pong"
:: Send new version to server
scp -r "%GODOT_PATH%/Exports/Ultra Pong Linux" ultra-pong-client:/home/Felipe-Cruz/ultra-pong
:: Clean and rebuild Docker image
ssh ultra-pong-client "bash -c ""sudo docker container prune -f && sudo docker image rm  -f ultra-pong-client || true && sudo docker build -t ultra-pong-client ."""
pause