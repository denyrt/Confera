# Technical Assignment

> This document preserves the original technical assignment for the **Confera** project. The requirements below have been kept as close as possible to the original specification while being translated into natural English.

## Task Context

You are working for a company that develops an application for managing conference room bookings and rentals. Your task is to create an API for managing conference rooms and bookings, as well as calculating rental costs.

## Problem Statement

The company rents conference rooms to businesses. A simple API needs to be developed that allows clients to search for available rooms, book them, and calculate rental costs based on the booking time and selected services.

## Technical Requirements

### API Endpoints

1. Add a conference room:

   * Input: Room name (for example, "Room A"), capacity (for example, 50 people), a list of available services (for example, a projector priced at 500 UAH and Wi-Fi priced at 300 UAH), and the base hourly rental rate (for example, 2,000 UAH).
   * Output: Confirmation that the room has been successfully created, including its unique ID.

2. Edit conference room information:

   * Input: Room ID and updated room data (for example, changing the rental rate to 2,500 UAH or adding a "Sound" service priced at 700 UAH).
   * Output: Confirmation that the room has been successfully updated.

3. Delete a conference room:

   * Input: Room ID.
   * Output: Confirmation that the room has been deleted.

4. Search for available conference rooms:

   * Input: Date, time range, and required capacity (for example, September 1, 2024, from 10:00 to 14:00, with a capacity of 50 people).
   * Output: A list of available conference rooms.

5. Book a conference room:

   * Input:

     * room ID;
     * booking date and time;
     * duration;
     * selected services.

   * Output:

     * booking confirmation including the calculated total rental cost.

### Initial Data

* Conference rooms:

  * Room A: capacity of 50 people, base rental rate of 2,000 UAH per hour.
  * Room B: capacity of 100 people, base rental rate of 3,500 UAH per hour.
  * Room C: capacity of 30 people, base rental rate of 1,500 UAH per hour.

* Services:

  * Projector: 500 UAH.
  * Wi-Fi: 300 UAH.
  * Sound: 700 UAH.

### Rental Cost Calculation

The rental cost depends on the booking time:

* Standard hours (09:00–18:00): base room rental rate.
* Evening hours (18:00–23:00): 20% discount on the room rental rate.
* Morning hours (06:00–09:00): 10% discount.
* Peak hours (12:00–14:00): 15% surcharge.

### Additional Requirements

1. Clean code and scalability:

   Follow the practices described in Robert C. Martin's *Clean Code* when implementing the solution.
   The project is expected to grow in the future, so it is important for the solution to be scalable, secure, and resilient.
   Provide an appropriate level of security to prevent issues affecting clients who will use the API.

2. Reports and analytics:

   Design and add reports that would provide useful insights for the business.

### Bonus Points

* A complete Git README.
* Code comments.
* API documentation using Swagger.
