/*
 * Intercept Suite - Main Header
 *
 * Defines common structures, constants and function declarations
 * for the TLS intercept proxy.
 */

#ifndef INTERCEPT_PROXY_H
#define INTERCEPT_PROXY_H

#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <process.h>
#include <iphlpapi.h>  /* For IP address validation */

#include <openssl/ssl.h>
#include <openssl/err.h>
#include <openssl/x509.h>
#include <openssl/x509v3.h>
#include <openssl/pem.h>
#include <openssl/bio.h>
#include <openssl/rsa.h>

/* Include DLL interface header for callback typedefs */
#include "tls_proxy_dll.h"

/* Define certificate error codes if not available in this OpenSSL version */
#ifndef SSL_R_TLSV1_ALERT_BAD_CERTIFICATE
#define SSL_R_TLSV1_ALERT_BAD_CERTIFICATE 0x304
#endif

#ifndef SSL_R_CERTIFICATE_VERIFY_FAILED
#define SSL_R_CERTIFICATE_VERIFY_FAILED 0x14B
#endif

/* OpenSSL Applink reference */
void **__cdecl OPENSSL_Applink(void);

/* Default Constants */
#define DEFAULT_PROXY_PORT 4444
#define DEFAULT_BIND_ADDR "127.0.0.1"
#define DEFAULT_LOGFILE "tls_proxy.log"
#define BUFFER_SIZE 16384
/* Only define MAX_HOSTNAME_LEN if not already defined */
#ifndef MAX_HOSTNAME_LEN
#define MAX_HOSTNAME_LEN 256
#endif
#define MAX_FILEPATH_LEN 512
#define MAX_IP_ADDR_LEN 46  /* Max length for IPv6 addresses */
#define CERT_EXPIRY_DAYS 365
#define CA_CERT_FILE "Intercept_Suite_Cert.pem"
#define CA_KEY_FILE "Intercept_Suite_key.key"

/* Windows-specific defines and typedefs */
typedef SOCKET socket_t;
/* Only define socklen_t if not already defined in system headers */
#ifndef _SOCKLEN_T_DEFINED
typedef unsigned int socklen_t;
#endif
#define THREAD_RETURN_TYPE unsigned __stdcall
#define THREAD_RETURN return 0
#define close_socket(s) closesocket(s)
#define THREAD_HANDLE HANDLE
#define CREATE_THREAD(handle, func, arg) handle = (HANDLE)_beginthreadex(NULL, 0, func, arg, 0, NULL)
#define JOIN_THREAD(handle) WaitForSingleObject(handle, INFINITE); CloseHandle(handle)
#define SLEEP(ms) Sleep(ms)

/* Define SSL error reason codes if not available */
#ifndef SSL_R_UNEXPECTED_EOF_WHILE_READING
#define SSL_R_UNEXPECTED_EOF_WHILE_READING 0x14A
#endif

/* Define certificate error codes if not available in this OpenSSL version */
#ifndef SSL_R_TLSV1_ALERT_BAD_CERTIFICATE
#define SSL_R_TLSV1_ALERT_BAD_CERTIFICATE 0x304
#endif

#ifndef SSL_R_CERTIFICATE_VERIFY_FAILED
#define SSL_R_CERTIFICATE_VERIFY_FAILED 0x14B
#endif

/* Structures */
typedef struct {
    socket_t client_sock;
    struct sockaddr_in client_addr;
} client_info;

typedef struct {
    socket_t client_sock;
    socket_t server_sock;
    SSL *client_ssl;
    SSL *server_ssl;
    char target_host[MAX_HOSTNAME_LEN];
    int target_port;
} connection_info;

typedef struct {
    SSL *src;
    SSL *dst;
    char direction[32]; /* Use a char array instead of string pointer */
    char src_ip[MAX_IP_ADDR_LEN];
    char dst_ip[MAX_IP_ADDR_LEN];
    int dst_port;
    int connection_id;
} forward_info;

/* TCP forwarding info without SSL */
typedef struct {
    socket_t src;
    socket_t dst;
    char direction[32];
    char src_ip[MAX_IP_ADDR_LEN];
    char dst_ip[MAX_IP_ADDR_LEN];
    int dst_port;
    int connection_id;
} forward_tcp_info;

/* Configuration structure */
typedef struct {
    int port;                       /* Port to listen on */
    char bind_addr[MAX_IP_ADDR_LEN];/* IP address to bind to */
    char log_file[MAX_FILEPATH_LEN];/* Path to log file */
    FILE *log_fp;                   /* Log file pointer */
    int verbose;                    /* Flag for verbose output */
} proxy_config;

/* Server thread control */
typedef struct {
    int should_stop;              /* Flag to signal server thread to stop */
    HANDLE thread_handle;         /* Server thread handle */
    CRITICAL_SECTION cs;         /* Critical section for thread safety */
    socket_t server_sock;        /* Server socket */
} server_thread_t;

/* Interception support structures and enums */

/* Interception direction flags */
typedef enum {
    INTERCEPT_NONE = 0,
    INTERCEPT_CLIENT_TO_SERVER = 1,
    INTERCEPT_SERVER_TO_CLIENT = 2,
    INTERCEPT_BOTH = 3
} intercept_direction_t;

/* Interception response actions */
typedef enum {
    INTERCEPT_ACTION_FORWARD = 0,
    INTERCEPT_ACTION_DROP = 1,
    INTERCEPT_ACTION_MODIFY = 2
} intercept_action_t;

/* Interception data structure */
typedef struct {
    int connection_id;
    char direction[32];
    char src_ip[MAX_IP_ADDR_LEN];
    char dst_ip[MAX_IP_ADDR_LEN];
    int dst_port;
    unsigned char *data;
    int data_length;
    int is_waiting_for_response;
    HANDLE response_event;
    intercept_action_t action;
    unsigned char *modified_data;
    int modified_length;
} intercept_data_t;

/* Global interception configuration */
typedef struct {
    intercept_direction_t enabled_directions;
    int is_interception_enabled;
    CRITICAL_SECTION intercept_cs;
} intercept_config_t;

/* Global certificate references */
extern X509 *ca_cert;
extern EVP_PKEY *ca_key;

/* Global configuration */
extern proxy_config config;
extern server_thread_t g_server;

/* Global interception configuration */
extern intercept_config_t g_intercept_config;

/* Global callback functions (defined in main.c) */
extern log_callback_t g_log_callback;
extern status_callback_t g_status_callback;
extern connection_callback_t g_connection_callback;
extern stats_callback_t g_stats_callback;
extern disconnect_callback_t g_disconnect_callback;
extern intercept_callback_t g_intercept_callback;

/* Function prototypes */
int init_winsock(void);
void cleanup_winsock(void);
int start_proxy_server(void);
int validate_ip_address(const char *ip_addr);
void log_message(const char *format, ...);
void close_log_file(void);

/* Server thread function prototypes */
THREAD_RETURN_TYPE WINAPI run_server_thread(void* arg);

#endif /* TLS_PROXY_H */
