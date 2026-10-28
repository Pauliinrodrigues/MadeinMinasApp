import { PublicStoreAttendance } from '../../src/app/core/services/store-attendance-api.service';

export function publicAttendanceFixture(): PublicStoreAttendance {
  return {
    hours: Array.from({ length: 7 }, (_, index) => ({
      day: index + 1,
      opensAt: '18:30',
      closesAt: index < 4 ? '23:30' : '23:59',
      isClosed: false,
    })),
    paymentMethods: ['Card', 'Cash', 'Pix'],
    deliveryEstimateMessage: 'O prazo de entrega pode variar de acordo com a demanda.',
    timeZone: 'America/Sao_Paulo',
    isOpen: true,
    isPaused: false,
    acceptingOrders: true,
    checkedAt: new Date().toISOString(),
    nextChangeAt: null,
  };
}
