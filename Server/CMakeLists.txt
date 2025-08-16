cmake_minimum_required(VERSION 3.28)
project(Ultra_Pong_Server)

# Use C++23 with modern compiler support
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Enable all warnings and modern C++ features
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -Wall -Wextra")

# Add ENet subdirectory
add_subdirectory(enet-1.3.18)

# Create your executable
add_executable(Ultra_Pong_Server main.cpp stun.cpp)

# Include ENet headers
target_include_directories(Ultra_Pong_Server PRIVATE enet-1.3.18/include)

# Link libraries (Windows-specific networking libraries added)
if(WIN32)
    target_link_libraries(Ultra_Pong_Server enet ws2_32 winmm)
else()
    target_link_libraries(Ultra_Pong_Server enet)
endif()