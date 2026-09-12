import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Modal from 'Components/Modal/Modal';
import ChangeBookModalContentConnector from './ChangeBookModalContentConnector';

class ChangeBookModal extends Component {

  //
  // Render

  render() {
    const {
      isOpen,
      onModalClose,
      ...otherProps
    } = this.props;

    return (
      <Modal
        isOpen={isOpen}
        onModalClose={onModalClose}
      >
        {
          isOpen ?
            <ChangeBookModalContentConnector
              {...otherProps}
              onModalClose={onModalClose}
            /> :
            null
        }
      </Modal>
    );
  }
}

ChangeBookModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ChangeBookModal;
