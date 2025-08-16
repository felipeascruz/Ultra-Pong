#include <iostream>
#include <exception>
#include "stun.h"  // Include header file instead of .cpp

int main() {
    try {
        STUNServer server;

        server.initialize();
        std::cout << "STUN server initialized successfully" << std::endl;

        server.run();
    }
    catch (const STUNServerException& e) {
        std::cerr << e.what() << std::endl;
        return 1;
    }
    catch (const std::exception& e) {
        std::cerr << e.what() << std::endl;
        return 2;
    }
    catch (...) {
        std::cerr << "Unexpected Exception" << std::endl;
        return 3;
    }

    return 0;
}